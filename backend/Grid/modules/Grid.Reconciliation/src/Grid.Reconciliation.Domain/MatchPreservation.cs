using System;
using System.Collections.Generic;
using System.Linq;

namespace Grid.Reconciliation;

public class MatchPreservationResult
{
    public bool Passed { get; set; } = true;
    public int PreservedGroupsCount { get; set; }
    public int LostGroupsCount { get; set; }
    public int DegradedGroupsCount { get; set; }
    public int ChangedMembersGroupsCount { get; set; }
    public List<string> Violations { get; set; } = [];
}

public static class MatchPreservation
{
    public static MatchPreservationResult CheckPreservation(ReconcileResult baseResult, ReconcileResult candidateResult)
    {
        var result = new MatchPreservationResult();

        // Index candidate groups by member records
        // A candidate group member record set signature: e.g. "LEFT:1,2|RIGHT:4"
        var candidateGroupLookup = new Dictionary<string, MatchGroupResult>(StringComparer.Ordinal);
        var candidateMemberToGroup = new Dictionary<(string Dataset, int Row), MatchGroupResult>();

        foreach (var candGroup in candidateResult.MatchedGroups)
        {
            var sig = MemberSignature(candGroup.Records);
            candidateGroupLookup[sig] = candGroup;
            foreach (var (dataset, rows) in candGroup.Records)
            {
                foreach (var r in rows)
                {
                    candidateMemberToGroup[(dataset, r)] = candGroup;
                }
            }
        }

        foreach (var baseGroup in baseResult.MatchedGroups)
        {
            var baseSig = MemberSignature(baseGroup.Records);
            var baseIsPartial = IsPartial(baseGroup);

            if (candidateGroupLookup.TryGetValue(baseSig, out var matchedCandGroup))
            {
                var candIsPartial = IsPartial(matchedCandGroup);
                if (!baseIsPartial && candIsPartial)
                {
                    // Full match became partial match - regression!
                    result.DegradedGroupsCount++;
                    result.Violations.Add($"Base full-match group '{baseGroup.Rule}' with members ({baseSig}) degraded to partial match.");
                }
                else
                {
                    result.PreservedGroupsCount++;
                }
            }
            else
            {
                // Group members were either lost or altered
                var anyMemberFound = false;
                MatchGroupResult? firstFoundGroup = null;

                foreach (var (dataset, rows) in baseGroup.Records)
                {
                    foreach (var r in rows)
                    {
                        if (candidateMemberToGroup.TryGetValue((dataset, r), out var foundGroup))
                        {
                            anyMemberFound = true;
                            if (firstFoundGroup == null) firstFoundGroup = foundGroup;
                        }
                    }
                }

                if (!anyMemberFound)
                {
                    result.LostGroupsCount++;
                    result.Violations.Add($"Base match group '{baseGroup.Rule}' with members ({baseSig}) was lost completely in candidate.");
                }
                else
                {
                    result.ChangedMembersGroupsCount++;
                    result.Violations.Add($"Base match group '{baseGroup.Rule}' with members ({baseSig}) had its member grouping altered in candidate.");
                }
            }
        }

        // Check that candidate didn't introduce unexpected record duplication
        var candidateRecordSeen = new HashSet<(string Dataset, int Row)>();
        foreach (var candGroup in candidateResult.MatchedGroups)
        {
            foreach (var (dataset, rows) in candGroup.Records)
            {
                foreach (var r in rows)
                {
                    if (!candidateRecordSeen.Add((dataset, r)))
                    {
                        result.Violations.Add($"Candidate matched record {dataset} row {r} multiple times across different groups.");
                    }
                }
            }
        }

        result.Passed = result.LostGroupsCount == 0 &&
                        result.DegradedGroupsCount == 0 &&
                        result.ChangedMembersGroupsCount == 0 &&
                        result.Violations.Count == 0;

        return result;
    }

    public static bool IsPartial(MatchGroupResult group)
    {
        return string.Equals(group.RuleType, "partial", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(group.Evidence?.Comparison, "partial_match", StringComparison.OrdinalIgnoreCase);
    }

    public static string MemberSignature(IReadOnlyDictionary<string, List<int>> records)
    {
        return string.Join("|", records
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}:{string.Join(",", kv.Value.OrderBy(x => x))}"));
    }
}
