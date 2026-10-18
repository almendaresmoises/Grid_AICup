using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Grid.Reconciliation;

public class EvidenceRecord
{
    public string EvidenceId { get; set; } = "";
    public string DatasetCode { get; set; } = "";
    public int RowIndex { get; set; }
    public string Reason { get; set; } = "";
    public Dictionary<string, string> Record { get; set; } = [];
    public string? CounterpartDatasetCode { get; set; }
    public int? CounterpartRowIndex { get; set; }
    public Dictionary<string, string>? CounterpartRecord { get; set; }
    public string? ObservedDifference { get; set; }
}

public class AnalysisEvidenceManifest
{
    public Guid ProfileId { get; set; }
    public int ProfileVersion { get; set; }
    public Guid SourceRunId { get; set; }
    public string EngineVersion { get; set; } = "v1";
    public int TotalSourceRecords { get; set; }
    public int BaseFullMatchRecords { get; set; }
    public int BasePartialMatchRecords { get; set; }
    public int BaseUnresolvedRecords { get; set; }
    public int BaseAmbiguousRecords { get; set; }
    public bool HasNoUnresolved => BaseUnresolvedRecords == 0;
    public List<DiagnosisItemDto> InitialDiagnoses { get; set; } = [];
    public List<EvidenceRecord> StratifiedEvidence { get; set; } = [];
    public Dictionary<string, int> ReasonDistribution { get; set; } = [];
}

public static class AnalysisEvidenceBuilder
{
    public static AnalysisEvidenceManifest BuildManifest(
        Profile profile,
        ProfileDefinition definition,
        ReconciliationRun sourceRun,
        ReconcileResult baseResult,
        Dictionary<string, List<Dictionary<string, string>>> datasets)
    {
        var manifest = new AnalysisEvidenceManifest
        {
            ProfileId = profile.Id,
            ProfileVersion = profile.Version,
            SourceRunId = sourceRun.Id,
            TotalSourceRecords = datasets.Values.Sum(rows => rows.Count),
        };

        var partial = baseResult.MatchedGroups
            .Where(MatchPreservation.IsPartial)
            .Sum(g => g.Records.Values.Sum(r => r.Count));
        manifest.BasePartialMatchRecords = partial;
        manifest.BaseFullMatchRecords = baseResult.Statistics.MatchedRecords - partial;
        manifest.BaseUnresolvedRecords = baseResult.Statistics.UnresolvedRecords;
        manifest.BaseAmbiguousRecords = baseResult.Statistics.AmbiguousRecords;

        // Reason distribution
        manifest.ReasonDistribution = baseResult.Unresolved
            .GroupBy(u => u.Reason, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count());

        if (manifest.HasNoUnresolved)
        {
            manifest.InitialDiagnoses.Add(new DiagnosisItemDto
            {
                Category = "NoUnresolvedRecords",
                Summary = "All records in the selected run are already matched. No unresolved optimization needed.",
                AffectedRecordCount = 0,
            });
            return manifest;
        }

        // Known key mappings from profile definition
        var refFieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var amtFieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dtFieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var codes = datasets.Keys.OrderBy(c => c).ToList();
        var primaryCode = codes.FirstOrDefault() ?? "LEFT";
        var secondaryCode = codes.Count > 1 ? codes[1] : primaryCode;

        var exactRule = definition.Rules?.FirstOrDefault(r => r.Type == "exact" && r.Datasets.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(codes));
        if (exactRule?.Keys != null)
        {
            foreach (var k in exactRule.Keys)
            {
                if (k.Type == "text" && k.Fields != null && refFieldMap.Count == 0)
                {
                    var firstVal = k.Fields.Values.FirstOrDefault() ?? "";
                    if (!firstVal.Contains("company", StringComparison.OrdinalIgnoreCase) && !firstVal.Contains("branch", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var (ds, f) in k.Fields) refFieldMap[ds] = f;
                    }
                }
                else if (k.Type == "amount" && k.Fields != null && amtFieldMap.Count == 0)
                {
                    foreach (var (ds, f) in k.Fields) amtFieldMap[ds] = f;
                }
                else if (k.Type == "date" && k.Fields != null && dtFieldMap.Count == 0)
                {
                    foreach (var (ds, f) in k.Fields) dtFieldMap[ds] = f;
                }
            }
        }

        string GetRowRef(Dictionary<string, string> row, string code)
        {
            var f = refFieldMap.GetValueOrDefault(code);
            return (!string.IsNullOrEmpty(f) && row.TryGetValue(f, out var v))
                ? v
                : FindFirstField(row, "reference", "ref", "order_id", "transaction_id", "doc_no", "document_no", "belnr", "ticket", "invoice", "inv", "check", "chk", "pan", "card_number");
        }

        string GetRowAmt(Dictionary<string, string> row, string code)
        {
            var f = amtFieldMap.GetValueOrDefault(code);
            return (!string.IsNullOrEmpty(f) && row.TryGetValue(f, out var v))
                ? v
                : FindFirstField(row, "amount", "total", "net_amount", "gross_amount", "charge_amount", "wrbtr", "dmbtr", "val");
        }

        string GetRowDate(Dictionary<string, string> row, string code)
        {
            var f = dtFieldMap.GetValueOrDefault(code);
            return (!string.IsNullOrEmpty(f) && row.TryGetValue(f, out var v))
                ? v
                : FindFirstField(row, "posting_date", "value_date", "statement_date", "tx_date", "transaction_date", "date", "bldat", "budat");
        }

        // Bounded stratified evidence sampling (max 120 records total)
        var evidenceList = new List<EvidenceRecord>();
        var evCounter = 1;

        var unresolvedPrimary = baseResult.Unresolved
            .Where(u => u.Dataset == primaryCode)
            .Take(60)
            .ToList();

        var unresolvedSecondary = baseResult.Unresolved
            .Where(u => u.Dataset == secondaryCode)
            .ToDictionary(u => u.CsvRow);

        var usedSecondaryRows = new HashSet<int>();

        // Pair candidate rows if possible
        foreach (var unres in unresolvedPrimary)
        {
            var rowA = unres.Record;
            var refA = GetRowRef(rowA, primaryCode);
            var amtA = GetRowAmt(rowA, primaryCode);
            var dtA = GetRowDate(rowA, primaryCode);

            // Search for closest counterpart in secondary dataset
            int? bestMatchRow = null;
            Dictionary<string, string>? bestMatchRecord = null;
            string? bestDiff = null;

            foreach (var (candRowIdx, candUnres) in unresolvedSecondary)
            {
                if (usedSecondaryRows.Contains(candRowIdx)) continue;
                var rowB = candUnres.Record;

                // Check scope keys first
                if (rowA.TryGetValue("company_code", out var compA) && rowB.TryGetValue("company_code", out var compB) &&
                    !string.Equals(compA.Trim(), compB.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                var refB = GetRowRef(rowB, secondaryCode);
                var amtB = GetRowAmt(rowB, secondaryCode);
                var dtB = GetRowDate(rowB, secondaryCode);

                var refSame = !string.IsNullOrEmpty(refA) && !string.IsNullOrEmpty(refB) &&
                              string.Equals(refA.Trim(), refB.Trim(), StringComparison.OrdinalIgnoreCase);
                var amtSame = !string.IsNullOrEmpty(amtA) && !string.IsNullOrEmpty(amtB) &&
                              string.Equals(amtA.Trim(), amtB.Trim(), StringComparison.OrdinalIgnoreCase);
                var dtSame = !string.IsNullOrEmpty(dtA) && !string.IsNullOrEmpty(dtB) &&
                             string.Equals(dtA.Trim(), dtB.Trim(), StringComparison.OrdinalIgnoreCase);

                if (refSame && amtSame && !dtSame)
                {
                    bestMatchRow = candRowIdx;
                    bestMatchRecord = rowB;
                    bestDiff = $"Settlement date lag: {primaryCode}={dtA} vs {secondaryCode}={dtB} (identical reference '{refA}' and amount {amtA})";
                    break;
                }
                else if (refSame && dtSame && !amtSame)
                {
                    bestMatchRow = candRowIdx;
                    bestMatchRecord = rowB;
                    bestDiff = $"Amount/fee variance: {primaryCode}={amtA} vs {secondaryCode}={amtB} (identical reference '{refA}' and date {dtA})";
                    break;
                }
                else if (amtSame && dtSame && !refSame)
                {
                    bestMatchRow = candRowIdx;
                    bestMatchRecord = rowB;
                    bestDiff = $"Reference typo/divergence: '{refA}' vs '{refB}' (identical amount {amtA} and date {dtA})";
                    break;
                }
                else if (refSame)
                {
                    bestMatchRow = candRowIdx;
                    bestMatchRecord = rowB;
                    bestDiff = $"Identical reference '{refA.Trim()}', possible date or amount variance ({primaryCode}: {amtA}, {dtA}; {secondaryCode}: {amtB}, {dtB})";
                    break;
                }
            }

            if (bestMatchRow.HasValue)
            {
                usedSecondaryRows.Add(bestMatchRow.Value);
            }

            var evRecord = new EvidenceRecord
            {
                EvidenceId = $"ev-{evCounter++}",
                DatasetCode = primaryCode,
                RowIndex = unres.CsvRow,
                Reason = unres.Reason,
                Record = rowA.Take(20).ToDictionary(k => k.Key, v => v.Value),
            };

            if (bestMatchRow.HasValue && bestMatchRecord != null)
            {
                evRecord.CounterpartDatasetCode = secondaryCode;
                evRecord.CounterpartRowIndex = bestMatchRow.Value;
                evRecord.CounterpartRecord = bestMatchRecord.Take(20).ToDictionary(k => k.Key, v => v.Value);
                evRecord.ObservedDifference = bestDiff;
            }

            evidenceList.Add(evRecord);
            if (evidenceList.Count >= 60) break;
        }

        // Add remaining unpaired secondary records so evidence reflects both datasets
        foreach (var (candRowIdx, candUnres) in unresolvedSecondary)
        {
            if (usedSecondaryRows.Contains(candRowIdx)) continue;
            evidenceList.Add(new EvidenceRecord
            {
                EvidenceId = $"ev-{evCounter++}",
                DatasetCode = secondaryCode,
                RowIndex = candUnres.CsvRow,
                Reason = candUnres.Reason,
                Record = candUnres.Record.Take(20).ToDictionary(k => k.Key, v => v.Value),
                ObservedDifference = "Unpaired record in secondary dataset"
            });
            if (evidenceList.Count >= 80) break;
        }

        // Add some representative matched rows to protect constraints (up to 20)
        var matchedSample = baseResult.MatchedGroups.Take(20).ToList();
        foreach (var group in matchedSample)
        {
            foreach (var (ds, rList) in group.Records)
            {
                if (rList.Count > 0 && (rList[0] - 2) >= 0 && (rList[0] - 2) < datasets[ds].Count)
                {
                    var rIdx = rList[0] - 2;
                    evidenceList.Add(new EvidenceRecord
                    {
                        EvidenceId = $"ev-matched-{evCounter++}",
                        DatasetCode = ds,
                        RowIndex = rList[0],
                        Reason = "Matched",
                        Record = datasets[ds][rIdx].Take(20).ToDictionary(k => k.Key, v => v.Value),
                        ObservedDifference = $"Matched under rule '{group.Rule}' ({group.RuleType})",
                    });
                }
            }
        }

        manifest.StratifiedEvidence = evidenceList;

        // Build diagnoses
        var pairedDifferences = evidenceList.Where(e => e.CounterpartRowIndex != null).ToList();
        if (pairedDifferences.Count > 0)
        {
            manifest.InitialDiagnoses.Add(new DiagnosisItemDto
            {
                Category = "FormatOrToleranceDifferences",
                Summary = $"{pairedDifferences.Count} candidate record pairs show settlement date lag, fee variance, or reference typos.",
                AffectedRecordCount = pairedDifferences.Count,
                SampleEvidenceIds = pairedDifferences.Select(e => e.EvidenceId).Take(5).ToList(),
            });
        }

        var missingCounterparts = evidenceList.Count(e => e.CounterpartRowIndex == null && e.Reason != "Matched");
        if (missingCounterparts > 0)
        {
            manifest.InitialDiagnoses.Add(new DiagnosisItemDto
            {
                Category = "MissingCounterpart",
                Summary = $"{missingCounterparts} sampled records have no candidate counterpart in other datasets (unilateral transactions).",
                AffectedRecordCount = missingCounterparts,
                SampleEvidenceIds = evidenceList.Where(e => e.CounterpartRowIndex == null && e.Reason != "Matched").Select(e => e.EvidenceId).Take(5).ToList(),
            });
        }

        return manifest;
    }

    private static string FindFirstField(Dictionary<string, string> row, params string[] candidateNames)
    {
        foreach (var cand in candidateNames)
        {
            foreach (var (key, val) in row)
            {
                if (key.Equals(cand, StringComparison.OrdinalIgnoreCase) || key.Contains(cand, StringComparison.OrdinalIgnoreCase))
                    return val;
            }
        }
        return "";
    }
}
