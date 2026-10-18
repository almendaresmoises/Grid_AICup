using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grid.Reconciliation;
using Xunit;

namespace Grid.Reconciliation;

public class SapGlBankDemoDatasetTests
{
    private static Dictionary<string, List<Dictionary<string, string>>> LoadCsvBatch(string glPath, string bankPath)
    {
        return new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["SAP_GL"] = ParseCsv(glPath),
            ["BANK_STMT"] = ParseCsv(bankPath)
        };
    }

    private static List<Dictionary<string, string>> ParseCsv(string path)
    {
        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0) return [];

        var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        var rows = new List<Dictionary<string, string>>();

        for (var i = 1; i < lines.Count; i++)
        {
            var parts = lines[i].Split(',');
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var j = 0; j < headers.Length && j < parts.Length; j++)
            {
                dict[headers[j]] = parts[j].Trim();
            }
            rows.Add(dict);
        }

        return rows;
    }

    [Fact]
    public void Set1_October_Baseline_Matches_Clean_Records_And_Exposes_Variances()
    {
        var rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../demo-data"));
        var glFile = Path.Combine(rootDir, "set-1-october", "sap_gl_october_2026.csv");
        var bankFile = Path.Combine(rootDir, "set-1-october", "bank_stmt_october_2026.csv");
        var profileFile = Path.Combine(rootDir, "profile-v1-baseline.json");

        if (!File.Exists(glFile)) return; // skip if relative path differs in test runner

        var data = LoadCsvBatch(glFile, bankFile);
        var profile = ProfileJson.Parse(File.ReadAllText(profileFile));

        var result = ReconciliationEngine.Reconcile(data, profile);

        // 14 clean exact matches
        Assert.Equal(14, result.MatchedGroups.Count);
        // Unmatched records exist due to date lag, wire fees, and typos
        Assert.True(result.Statistics.UnresolvedRecords > 0);
    }

    [Fact]
    public void Set1_With_Optimized_Passes_Recovers_Lag_Fees_And_Typos_With_Zero_Regressions()
    {
        var rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../demo-data"));
        var glFile = Path.Combine(rootDir, "set-1-october", "sap_gl_october_2026.csv");
        var bankFile = Path.Combine(rootDir, "set-1-october", "bank_stmt_october_2026.csv");
        var profileFile = Path.Combine(rootDir, "profile-v1-baseline.json");

        if (!File.Exists(glFile)) return;

        var data = LoadCsvBatch(glFile, bankFile);
        var baseProfile = ProfileJson.Parse(File.ReadAllText(profileFile));
        var baseResult = ReconciliationEngine.Reconcile(data, baseProfile);

        // Build candidate profile with 3 additive passes
        var candidateProfile = ProfileJson.Parse(File.ReadAllText(profileFile));

        // 1. Date tolerance pass (+/- 2 days, identical ref and amount)
        candidateProfile.Rules.Add(new RuleDefinition
        {
            Id = "PARTIAL_DATE_TOL",
            Type = "partial",
            Datasets = ["SAP_GL", "BANK_STMT"],
            Keys =
            [
                new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new() { ["SAP_GL"] = "reference", ["BANK_STMT"] = "bank_reference" } },
                new KeyDefinition { Type = "amount", MatchMode = "exact", Fields = new() { ["SAP_GL"] = "amount", ["BANK_STMT"] = "amount" } },
                new KeyDefinition { Type = "date", MatchMode = "fuzzy", Algorithm = "date_proximity", Tolerance = "2", MinScore = 0.8m, Fields = new() { ["SAP_GL"] = "posting_date", ["BANK_STMT"] = "value_date" } }
            ],
            MinScore = 0.8m
        });

        // 2. Fee tolerance pass (+/- 1.00 fee tolerance, identical ref and date)
        candidateProfile.Rules.Add(new RuleDefinition
        {
            Id = "PARTIAL_AMT_TOL",
            Type = "partial",
            Datasets = ["SAP_GL", "BANK_STMT"],
            Keys =
            [
                new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new() { ["SAP_GL"] = "reference", ["BANK_STMT"] = "bank_reference" } },
                new KeyDefinition { Type = "date", MatchMode = "exact", Fields = new() { ["SAP_GL"] = "posting_date", ["BANK_STMT"] = "value_date" } },
                new KeyDefinition { Type = "amount", MatchMode = "fuzzy", Algorithm = "numeric_best_practice", Tolerance = "1.00", MinScore = 0.8m, Fields = new() { ["SAP_GL"] = "amount", ["BANK_STMT"] = "amount" } }
            ],
            MinScore = 0.8m
        });

        // 3. Fuzzy reference pass (identical amount and date, fuzzy ref min score 0.80)
        candidateProfile.Rules.Add(new RuleDefinition
        {
            Id = "PARTIAL_FUZZY_REF",
            Type = "partial",
            Datasets = ["SAP_GL", "BANK_STMT"],
            Keys =
            [
                new KeyDefinition { Type = "amount", MatchMode = "exact", Fields = new() { ["SAP_GL"] = "amount", ["BANK_STMT"] = "amount" } },
                new KeyDefinition { Type = "date", MatchMode = "exact", Fields = new() { ["SAP_GL"] = "posting_date", ["BANK_STMT"] = "value_date" } },
                new KeyDefinition { Type = "text", MatchMode = "fuzzy", Algorithm = "damerau_levenshtein", MinScore = 0.80m, Fields = new() { ["SAP_GL"] = "reference", ["BANK_STMT"] = "bank_reference" } }
            ],
            MinScore = 0.8m
        });

        var candResult = ReconciliationEngine.Reconcile(data, candidateProfile);

        // Strict match preservation check
        var preservation = MatchPreservation.CheckPreservation(baseResult, candResult);
        Assert.True(preservation.Passed);
        Assert.Equal(0, preservation.LostGroupsCount);
        Assert.Empty(preservation.Violations);

        // Quantify impact: match count must significantly improve
        var impact = AnalysisImpact.Compute(baseResult, candResult, data);
        Assert.True(impact.RecoveredUnresolvedRecords >= 16); // recovered date lag, fees, and typos
        Assert.True(candResult.MatchedGroups.Count > baseResult.MatchedGroups.Count);
    }
}
