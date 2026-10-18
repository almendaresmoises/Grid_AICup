using System;
using System.Collections.Generic;
using System.Linq;

namespace Grid.Reconciliation;

public class DatasetRecordCounts
{
    public string DatasetCode { get; set; } = "";
    public int TotalRecords { get; set; }
    public int BaseFullMatchRecords { get; set; }
    public int BasePartialMatchRecords { get; set; }
    public int BaseUnresolvedRecords { get; set; }
    public int BaseAmbiguousRecords { get; set; }
    public int CandidateFullMatchRecords { get; set; }
    public int CandidatePartialMatchRecords { get; set; }
    public int CandidateUnresolvedRecords { get; set; }
    public int CandidateAmbiguousRecords { get; set; }
}

public class GroupContributionImpact
{
    public string GroupId { get; set; } = "";
    public int NewlyMatchedRecords { get; set; }
    public int NewlyMatchedGroups { get; set; }
    public int FullMatches { get; set; }
    public int PartialMatches { get; set; }
    public double IndependentAnyGainPoints { get; set; }
    public double MarginalAnyGainPoints { get; set; }
}

public class AnalysisImpactReport
{
    public int TotalRecords { get; set; }
    public int BaseFullMatchRecords { get; set; }
    public int BasePartialMatchRecords { get; set; }
    public int BaseUnresolvedRecords { get; set; }
    public int BaseAmbiguousRecords { get; set; }
    public int BaseMatchedGroups { get; set; }

    public int CandidateFullMatchRecords { get; set; }
    public int CandidatePartialMatchRecords { get; set; }
    public int CandidateUnresolvedRecords { get; set; }
    public int CandidateAmbiguousRecords { get; set; }
    public int CandidateMatchedGroups { get; set; }

    public int RecoveredUnresolvedRecords { get; set; }
    public int ProtectedMatchedRecords { get; set; }

    public double BaseFullMatchPercentage => TotalRecords > 0 ? (100.0 * BaseFullMatchRecords / TotalRecords) : 0;
    public double BasePartialMatchPercentage => TotalRecords > 0 ? (100.0 * BasePartialMatchRecords / TotalRecords) : 0;
    public double BaseAnyMatchPercentage => TotalRecords > 0 ? (100.0 * (BaseFullMatchRecords + BasePartialMatchRecords) / TotalRecords) : 0;

    public double CandidateFullMatchPercentage => TotalRecords > 0 ? (100.0 * CandidateFullMatchRecords / TotalRecords) : 0;
    public double CandidatePartialMatchPercentage => TotalRecords > 0 ? (100.0 * CandidatePartialMatchRecords / TotalRecords) : 0;
    public double CandidateAnyMatchPercentage => TotalRecords > 0 ? (100.0 * (CandidateFullMatchRecords + CandidatePartialMatchRecords) / TotalRecords) : 0;

    public double FullImprovementPercentagePoints => CandidateFullMatchPercentage - BaseFullMatchPercentage;
    public double AnyImprovementPercentagePoints => CandidateAnyMatchPercentage - BaseAnyMatchPercentage;
    public double RecoveredUnresolvedPercentage => BaseUnresolvedRecords > 0 ? (100.0 * RecoveredUnresolvedRecords / BaseUnresolvedRecords) : 0;

    public List<DatasetRecordCounts> DatasetBreakdowns { get; set; } = [];
    public List<GroupContributionImpact> GroupContributions { get; set; } = [];
    public MatchPreservationResult Preservation { get; set; } = new();
}

public static class AnalysisImpact
{
    public static AnalysisImpactReport Compute(
        ReconcileResult baseResult,
        ReconcileResult candidateResult,
        IReadOnlyDictionary<string, List<Dictionary<string, string>>> datasets)
    {
        var totalRecords = datasets.Values.Sum(rows => rows.Count);

        // Base metrics
        var basePartial = baseResult.MatchedGroups
            .Where(MatchPreservation.IsPartial)
            .Sum(g => g.Records.Values.Sum(r => r.Count));
        var baseFull = baseResult.Statistics.MatchedRecords - basePartial;
        var baseUnresolved = baseResult.Statistics.UnresolvedRecords;
        var baseAmbiguous = baseResult.Statistics.AmbiguousRecords;

        // Candidate metrics
        var candPartial = candidateResult.MatchedGroups
            .Where(MatchPreservation.IsPartial)
            .Sum(g => g.Records.Values.Sum(r => r.Count));
        var candFull = candidateResult.Statistics.MatchedRecords - candPartial;
        var candUnresolved = candidateResult.Statistics.UnresolvedRecords;
        var candAmbiguous = candidateResult.Statistics.AmbiguousRecords;

        // Trace newly matched records from base unresolved
        var baseUnresolvedSet = baseResult.Unresolved
            .Select(u => (u.Dataset, u.CsvRow))
            .ToHashSet();

        var candidateMatchedRecordSet = new HashSet<(string Dataset, int RowIndex)>();
        foreach (var group in candidateResult.MatchedGroups)
        {
            foreach (var (dataset, rows) in group.Records)
            {
                foreach (var r in rows)
                {
                    candidateMatchedRecordSet.Add((dataset, r));
                }
            }
        }

        var baseMatchedRecordSet = new HashSet<(string Dataset, int RowIndex)>();
        foreach (var group in baseResult.MatchedGroups)
        {
            foreach (var (dataset, rows) in group.Records)
            {
                foreach (var r in rows)
                {
                    baseMatchedRecordSet.Add((dataset, r));
                }
            }
        }

        var newlyMatchedFromUnresolved = baseUnresolvedSet.Count(u => candidateMatchedRecordSet.Contains(u));
        var protectedMatched = baseMatchedRecordSet.Count(b => candidateMatchedRecordSet.Contains(b));

        var preservation = MatchPreservation.CheckPreservation(baseResult, candidateResult);

        // Per dataset breakdown
        var breakdowns = new List<DatasetRecordCounts>();
        foreach (var (code, rows) in datasets)
        {
            var dsTotal = rows.Count;

            var dsBasePartial = baseResult.MatchedGroups
                .Where(MatchPreservation.IsPartial)
                .Sum(g => g.Records.TryGetValue(code, out var list) ? list.Count : 0);
            var dsBaseMatchedTotal = baseResult.MatchedGroups
                .Sum(g => g.Records.TryGetValue(code, out var list) ? list.Count : 0);
            var dsBaseFull = dsBaseMatchedTotal - dsBasePartial;
            var dsBaseUnresolved = baseResult.Unresolved.Count(u => string.Equals(u.Dataset, code, StringComparison.Ordinal));
            var dsBaseAmbiguous = baseResult.Unresolved.Count(u => string.Equals(u.Dataset, code, StringComparison.Ordinal) && string.Equals(u.Reason, "Ambiguous", StringComparison.OrdinalIgnoreCase));

            var dsCandPartial = candidateResult.MatchedGroups
                .Where(MatchPreservation.IsPartial)
                .Sum(g => g.Records.TryGetValue(code, out var list) ? list.Count : 0);
            var dsCandMatchedTotal = candidateResult.MatchedGroups
                .Sum(g => g.Records.TryGetValue(code, out var list) ? list.Count : 0);
            var dsCandFull = dsCandMatchedTotal - dsCandPartial;
            var dsCandUnresolved = candidateResult.Unresolved.Count(u => string.Equals(u.Dataset, code, StringComparison.Ordinal));
            var dsCandAmbiguous = candidateResult.Unresolved.Count(u => string.Equals(u.Dataset, code, StringComparison.Ordinal) && string.Equals(u.Reason, "Ambiguous", StringComparison.OrdinalIgnoreCase));

            breakdowns.Add(new DatasetRecordCounts
            {
                DatasetCode = code,
                TotalRecords = dsTotal,
                BaseFullMatchRecords = dsBaseFull,
                BasePartialMatchRecords = dsBasePartial,
                BaseUnresolvedRecords = dsBaseUnresolved,
                BaseAmbiguousRecords = dsBaseAmbiguous,
                CandidateFullMatchRecords = dsCandFull,
                CandidatePartialMatchRecords = dsCandPartial,
                CandidateUnresolvedRecords = dsCandUnresolved,
                CandidateAmbiguousRecords = dsCandAmbiguous,
            });
        }

        return new AnalysisImpactReport
        {
            TotalRecords = totalRecords,
            BaseFullMatchRecords = baseFull,
            BasePartialMatchRecords = basePartial,
            BaseUnresolvedRecords = baseUnresolved,
            BaseAmbiguousRecords = baseAmbiguous,
            BaseMatchedGroups = baseResult.Statistics.MatchedGroups,

            CandidateFullMatchRecords = candFull,
            CandidatePartialMatchRecords = candPartial,
            CandidateUnresolvedRecords = candUnresolved,
            CandidateAmbiguousRecords = candAmbiguous,
            CandidateMatchedGroups = candidateResult.Statistics.MatchedGroups,

            RecoveredUnresolvedRecords = newlyMatchedFromUnresolved,
            ProtectedMatchedRecords = protectedMatched,
            DatasetBreakdowns = breakdowns,
            Preservation = preservation,
        };
    }
}
