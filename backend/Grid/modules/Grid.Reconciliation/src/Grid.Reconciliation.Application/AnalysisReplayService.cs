using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Grid.Reconciliation;

public class ReplayEvaluationResult
{
    public ReconciliationAnalysisValidation Validation { get; set; } = new();
    public AnalysisImpactReport ImpactReport { get; set; } = new();
    public ProfileDefinition CompiledProfile { get; set; } = new();
    public List<RegressionVerdictDto> RegressionVerdicts { get; set; } = [];
}

public class AnalysisReplayService : ITransientDependency
{
    private readonly IRepository<RegressionBaseline, Guid> _baselines;
    private readonly IRepository<ReconciliationRun, Guid> _runs;

    public AnalysisReplayService(
        IRepository<RegressionBaseline, Guid> baselines,
        IRepository<ReconciliationRun, Guid> runs)
    {
        _baselines = baselines;
        _runs = runs;
    }

    public async Task<ReplayEvaluationResult> EvaluateCombinationAsync(
        Profile profile,
        ProfileDefinition baseProfile,
        List<ReconciliationAnalysisChange> allChanges,
        List<string> selectedGroupIds,
        Dictionary<string, List<Dictionary<string, string>>> sourceDatasets,
        Guid sourceRunId)
    {
        // 1. Compile candidate profile
        var selectedChanges = allChanges
            .Where(c => selectedGroupIds.Contains(c.DependencyGroupId))
            .ToList();

        var compiledProfile = CompileCandidateProfile(baseProfile, selectedChanges);

        // Validate compiled profile against source schemas
        ReconciliationEngine.ValidateProfile(compiledProfile, sourceDatasets);

        // 2. Deterministic replay on source run datasets
        var baseResult = ReconciliationEngine.Reconcile(sourceDatasets, baseProfile);
        var candResult = ReconciliationEngine.Reconcile(sourceDatasets, compiledProfile);

        var impact = AnalysisImpact.Compute(baseResult, candResult, sourceDatasets);

        // 3. Regression evaluation
        var regressionVerdicts = new List<RegressionVerdictDto>();

        // 3a. Original package baseline
        var baseline = await _baselines.FirstOrDefaultAsync(b => b.PackageId == profile.PackageId);
        if (baseline != null && !string.IsNullOrWhiteSpace(baseline.DatasetsJson) && !string.IsNullOrWhiteSpace(baseline.ProfileJson))
        {
            try
            {
                var baselineRows = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(baseline.DatasetsJson) ?? [];
                var baselineProfile = ProfileJson.Parse(baseline.ProfileJson);
                var prevBase = ReconciliationEngine.Reconcile(baselineRows, baselineProfile);
                var candBase = ReconciliationEngine.Reconcile(baselineRows, compiledProfile);
                var presBase = MatchPreservation.CheckPreservation(prevBase, candBase);

                regressionVerdicts.Add(new RegressionVerdictDto
                {
                    RunId = baseline.Id,
                    Description = "Original Package Baseline",
                    Passed = presBase.Passed,
                    Reason = presBase.Passed
                        ? "All previously matched baseline groups preserved"
                        : string.Join("; ", presBase.Violations.Take(3)),
                    TotalRecords = baselineRows.Values.Sum(r => r.Count),
                    MatchedRecords = candBase.Statistics.MatchedRecords,
                });
            }
            catch (Exception ex)
            {
                regressionVerdicts.Add(new RegressionVerdictDto
                {
                    RunId = baseline.Id,
                    Description = "Original Package Baseline",
                    Passed = false,
                    Reason = "Failed to replay baseline: " + ex.Message,
                });
            }
        }

        // 3b. Recent historical runs (up to 5 other successful runs)
        var recentRunsQuery = (await _runs.GetQueryableAsync())
            .Where(r => r.ProfileId == profile.Id && r.Status == RunStatus.Succeeded && r.Id != sourceRunId)
            .OrderByDescending(r => r.CreationTime)
            .Take(5);

        var recentRuns = recentRunsQuery.ToList();
        foreach (var run in recentRuns)
        {
            if (string.IsNullOrWhiteSpace(run.DatasetSnapshotJson) || string.IsNullOrWhiteSpace(run.DefinitionSnapshotJson))
                continue;

            try
            {
                var runRows = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(run.DatasetSnapshotJson) ?? [];
                var runProfile = ProfileJson.Parse(run.DefinitionSnapshotJson);
                var prevRun = ReconciliationEngine.Reconcile(runRows, runProfile);
                var candRun = ReconciliationEngine.Reconcile(runRows, compiledProfile);
                var presRun = MatchPreservation.CheckPreservation(prevRun, candRun);

                regressionVerdicts.Add(new RegressionVerdictDto
                {
                    RunId = run.Id,
                    Description = $"Historical Run v{run.ProfileVersion} ({run.CreationTime:yyyy-MM-dd HH:mm})",
                    Passed = presRun.Passed,
                    Reason = presRun.Passed
                        ? "All previously matched groups preserved"
                        : string.Join("; ", presRun.Violations.Take(3)),
                    TotalRecords = run.TotalRecords,
                    MatchedRecords = candRun.Statistics.MatchedRecords,
                });
            }
            catch (Exception ex)
            {
                regressionVerdicts.Add(new RegressionVerdictDto
                {
                    RunId = run.Id,
                    Description = $"Historical Run ({run.CreationTime:yyyy-MM-dd HH:mm})",
                    Passed = false,
                    Reason = "Failed to replay historical run: " + ex.Message,
                });
            }
        }

        // 4. Group contributions (independent and marginal)
        var cumulativeChanges = new List<ReconciliationAnalysisChange>();
        var prevCumulativeAnyPoints = impact.BaseAnyMatchPercentage;

        foreach (var groupId in selectedGroupIds)
        {
            var grpChanges = selectedChanges.Where(c => c.DependencyGroupId == groupId).ToList();
            if (grpChanges.Count == 0) continue;

            // Independent evaluation
            var indProfile = CompileCandidateProfile(baseProfile, grpChanges);
            var indResult = ReconciliationEngine.Reconcile(sourceDatasets, indProfile);
            var indImpact = AnalysisImpact.Compute(baseResult, indResult, sourceDatasets);

            // Marginal evaluation
            cumulativeChanges.AddRange(grpChanges);
            var cumProfile = CompileCandidateProfile(baseProfile, cumulativeChanges);
            var cumResult = ReconciliationEngine.Reconcile(sourceDatasets, cumProfile);
            var cumImpact = AnalysisImpact.Compute(baseResult, cumResult, sourceDatasets);

            var marginalGain = cumImpact.CandidateAnyMatchPercentage - prevCumulativeAnyPoints;
            prevCumulativeAnyPoints = cumImpact.CandidateAnyMatchPercentage;

            impact.GroupContributions.Add(new GroupContributionImpact
            {
                GroupId = groupId,
                NewlyMatchedRecords = indImpact.RecoveredUnresolvedRecords,
                NewlyMatchedGroups = Math.Max(0, indResult.Statistics.MatchedGroups - baseResult.Statistics.MatchedGroups),
                FullMatches = indImpact.CandidateFullMatchRecords - indImpact.BaseFullMatchRecords,
                PartialMatches = indImpact.CandidatePartialMatchRecords - indImpact.BasePartialMatchRecords,
                IndependentAnyGainPoints = indImpact.AnyImprovementPercentagePoints,
                MarginalAnyGainPoints = marginalGain,
            });
        }

        var allRegressionsPassed = regressionVerdicts.All(v => v.Passed);
        var overallPreserved = impact.Preservation.Passed && allRegressionsPassed;

        var validation = new ReconciliationAnalysisValidation(Guid.NewGuid())
        {
            SelectionHash = ComputeSelectionHash(selectedGroupIds),
            BaseHash = ComputeProfileHash(baseProfile),
            InputManifestHash = ComputeDatasetsHash(sourceDatasets),
            EngineVersion = "v1",
            State = "Completed",
            Coverage = "FullReplay",
            FullMatchCount = impact.CandidateFullMatchRecords,
            PartialMatchCount = impact.CandidatePartialMatchRecords,
            UnresolvedCount = impact.CandidateUnresolvedRecords,
            AmbiguousCount = impact.CandidateAmbiguousRecords,
            BaseFullMatchCount = impact.BaseFullMatchRecords,
            BasePartialMatchCount = impact.BasePartialMatchRecords,
            BaseUnresolvedCount = impact.BaseUnresolvedRecords,
            BaseAmbiguousCount = impact.BaseAmbiguousRecords,
            TotalRecords = impact.TotalRecords,
            PreservedMatches = overallPreserved,
            RegressionVerdictsJson = JsonSerializer.Serialize(regressionVerdicts, ProfileJson.Options),
            DetailedReportJson = JsonSerializer.Serialize(impact, ProfileJson.Options),
            CreationTime = DateTime.UtcNow,
            CompletionTime = DateTime.UtcNow,
        };

        return new ReplayEvaluationResult
        {
            Validation = validation,
            ImpactReport = impact,
            CompiledProfile = compiledProfile,
            RegressionVerdicts = regressionVerdicts,
        };
    }

    public static ProfileDefinition CompileCandidateProfile(
        ProfileDefinition baseProfile,
        List<ReconciliationAnalysisChange> changes)
    {
        var json = ProfileJson.Write(baseProfile);
        var candidate = ProfileJson.Parse(json);
        candidate.Rules = new List<RuleDefinition>(candidate.Rules);

        var existingRuleIds = candidate.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var change in changes)
        {
            if (string.Equals(change.OperationType, "append_rule", StringComparison.OrdinalIgnoreCase))
            {
                var rule = JsonSerializer.Deserialize<RuleDefinition>(change.OperationJson, ProfileJson.Options);
                if (rule != null && !string.IsNullOrWhiteSpace(rule.Id) && !existingRuleIds.Contains(rule.Id))
                {
                    existingRuleIds.Add(rule.Id);
                    candidate.Rules.Add(rule);
                }
            }
            else if (string.Equals(change.OperationType, "modify_rule", StringComparison.OrdinalIgnoreCase))
            {
                var rule = JsonSerializer.Deserialize<RuleDefinition>(change.OperationJson, ProfileJson.Options);
                if (rule != null)
                {
                    var idx = candidate.Rules.FindIndex(r => string.Equals(r.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) candidate.Rules[idx] = rule;
                }
            }
        }

        return candidate;
    }

    public static string ComputeSelectionHash(IEnumerable<string> selectedGroupIds)
    {
        var ordered = string.Join(",", selectedGroupIds.OrderBy(x => x, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("selection-v1:" + ordered)));
    }

    public static string ComputeProfileHash(ProfileDefinition profile)
    {
        var json = ProfileJson.Write(profile);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static string ComputeDatasetsHash(Dictionary<string, List<Dictionary<string, string>>> datasets)
    {
        var sb = new StringBuilder();
        foreach (var (code, rows) in datasets.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(code).Append(':').Append(rows.Count).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
