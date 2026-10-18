using System;
using System.Collections.Generic;
using Xunit;

namespace Grid.Reconciliation;

public class MatchPreservationAndImpactTests
{
    [Fact]
    public void Preserves_matches_when_candidate_retains_identical_baseline_groups()
    {
        var baseline = new ReconcileResult
        {
            MatchedGroups =
            [
                new MatchGroupResult
                {
                    Rule = "R1",
                    RuleType = "exact",
                    Records = new() { ["A"] = [2], ["B"] = [2] },
                    Key = ["1001"]
                }
            ],
            Unresolved =
            [
                new UnresolvedResult { Dataset = "A", CsvRow = 3, Reason = "no_match" },
                new UnresolvedResult { Dataset = "B", CsvRow = 3, Reason = "no_match" }
            ]
        };

        var candidate = new ReconcileResult
        {
            MatchedGroups =
            [
                new MatchGroupResult
                {
                    Rule = "R1",
                    RuleType = "exact",
                    Records = new() { ["A"] = [2], ["B"] = [2] },
                    Key = ["1001"]
                },
                new MatchGroupResult
                {
                    Rule = "R2",
                    RuleType = "exact",
                    Records = new() { ["A"] = [3], ["B"] = [3] },
                    Key = ["1002"]
                }
            ],
            Unresolved = []
        };

        var evaluation = MatchPreservation.CheckPreservation(baseline, candidate);
        Assert.True(evaluation.Passed);
        Assert.Equal(1, evaluation.PreservedGroupsCount);
        Assert.Equal(0, evaluation.LostGroupsCount);
        Assert.Equal(0, evaluation.DegradedGroupsCount);
        Assert.Equal(0, evaluation.ChangedMembersGroupsCount);
        Assert.Empty(evaluation.Violations);
    }

    [Fact]
    public void Fails_preservation_when_full_match_becomes_partial()
    {
        var baseline = new ReconcileResult
        {
            MatchedGroups =
            [
                new MatchGroupResult
                {
                    Rule = "R1",
                    RuleType = "exact",
                    Records = new() { ["A"] = [2], ["B"] = [2] },
                    Key = ["1001"]
                }
            ],
            Unresolved = []
        };

        var candidate = new ReconcileResult
        {
            MatchedGroups =
            [
                new MatchGroupResult
                {
                    Rule = "R1",
                    RuleType = "partial",
                    Score = 0.85m,
                    Records = new() { ["A"] = [2], ["B"] = [2] },
                    Key = ["1001"]
                }
            ],
            Unresolved = []
        };

        var evaluation = MatchPreservation.CheckPreservation(baseline, candidate);
        Assert.False(evaluation.Passed);
        Assert.Equal(1, evaluation.DegradedGroupsCount);
        Assert.Contains(evaluation.Violations, v => v.Contains("degraded"));
    }

    [Fact]
    public void Fails_preservation_when_previously_matched_group_is_lost()
    {
        var baseline = new ReconcileResult
        {
            MatchedGroups =
            [
                new MatchGroupResult
                {
                    Rule = "R1",
                    RuleType = "exact",
                    Records = new() { ["A"] = [2], ["B"] = [2] },
                    Key = ["1001"]
                }
            ],
            Unresolved = []
        };

        var candidate = new ReconcileResult
        {
            MatchedGroups = [],
            Unresolved =
            [
                new UnresolvedResult { Dataset = "A", CsvRow = 2, Reason = "no_match" },
                new UnresolvedResult { Dataset = "B", CsvRow = 2, Reason = "no_match" }
            ]
        };

        var evaluation = MatchPreservation.CheckPreservation(baseline, candidate);
        Assert.False(evaluation.Passed);
        Assert.Equal(1, evaluation.LostGroupsCount);
        Assert.Contains(evaluation.Violations, v => v.Contains("lost"));
    }

    [Fact]
    public void Fails_preservation_when_group_members_change()
    {
        var baseline = new ReconcileResult
        {
            MatchedGroups =
            [
                new MatchGroupResult
                {
                    Rule = "R1",
                    RuleType = "exact",
                    Records = new() { ["A"] = [2], ["B"] = [2] },
                    Key = ["1001"]
                }
            ],
            Unresolved = []
        };

        var candidate = new ReconcileResult
        {
            MatchedGroups =
            [
                new MatchGroupResult
                {
                    Rule = "R1",
                    RuleType = "exact",
                    Records = new() { ["A"] = [2], ["B"] = [4] }, // B changed from row 2 to 4
                    Key = ["1001"]
                }
            ],
            Unresolved = []
        };

        var evaluation = MatchPreservation.CheckPreservation(baseline, candidate);
        Assert.False(evaluation.Passed);
        Assert.Equal(1, evaluation.ChangedMembersGroupsCount);
        Assert.Contains(evaluation.Violations, v => v.Contains("member grouping altered"));
    }

    [Fact]
    public void Analysis_impact_computes_exact_record_weighted_metrics_and_deltas()
    {
        // 1000 total records: 500 in A, 500 in B
        // Base: 700 full, 100 partial, 200 unresolved (of which 50 ambiguous)
        // Candidate: 760 full, 120 partial, 120 unresolved
        // Full moves 70% -> 76% (+6 pp)
        // Any moves 80% -> 88% (+8 pp)
        // Recovered unresolved: 80 / 200 = 40%

        var baseMatched = new List<MatchGroupResult>();
        // 350 pairs = 700 full
        for (int i = 0; i < 350; i++)
        {
            baseMatched.Add(new MatchGroupResult
            {
                Rule = "R1",
                RuleType = "exact",
                Records = new() { ["A"] = [i + 2], ["B"] = [i + 2] },
                Key = [$"K_{i}"]
            });
        }
        // 50 pairs = 100 partial
        for (int i = 350; i < 400; i++)
        {
            baseMatched.Add(new MatchGroupResult
            {
                Rule = "R2",
                RuleType = "partial",
                Score = 0.9m,
                Records = new() { ["A"] = [i + 2], ["B"] = [i + 2] },
                Key = [$"K_{i}"]
            });
        }

        var baseUnresolved = new List<UnresolvedResult>();
        // 100 unresolved per dataset = 200 total (50 ambiguous = 25 per dataset)
        for (int i = 400; i < 500; i++)
        {
            baseUnresolved.Add(new UnresolvedResult { Dataset = "A", CsvRow = i + 2, Reason = i < 425 ? "ambiguous" : "no_match" });
            baseUnresolved.Add(new UnresolvedResult { Dataset = "B", CsvRow = i + 2, Reason = i < 425 ? "ambiguous" : "no_match" });
        }

        var baseline = new ReconcileResult
        {
            MatchedGroups = baseMatched,
            Unresolved = baseUnresolved,
            Statistics = new ReconcileStatistics
            {
                TotalRecords = 1000,
                MatchedRecords = 800,
                UnresolvedRecords = 200,
                AmbiguousRecords = 50,
                MatchedGroups = 400
            }
        };

        // Candidate preserves all 400 groups and adds 30 exact (60 records) and 10 partial (20 records)
        var candMatched = new List<MatchGroupResult>(baseMatched);
        for (int i = 400; i < 430; i++)
        {
            candMatched.Add(new MatchGroupResult
            {
                Rule = "R3_NEW",
                RuleType = "exact",
                Records = new() { ["A"] = [i + 2], ["B"] = [i + 2] },
                Key = [$"K_{i}"]
            });
        }
        for (int i = 430; i < 440; i++)
        {
            candMatched.Add(new MatchGroupResult
            {
                Rule = "R4_NEW",
                RuleType = "partial",
                Score = 0.85m,
                Records = new() { ["A"] = [i + 2], ["B"] = [i + 2] },
                Key = [$"K_{i}"]
            });
        }

        var candUnresolved = new List<UnresolvedResult>();
        for (int i = 440; i < 500; i++)
        {
            candUnresolved.Add(new UnresolvedResult { Dataset = "A", CsvRow = i + 2, Reason = "no_match" });
            candUnresolved.Add(new UnresolvedResult { Dataset = "B", CsvRow = i + 2, Reason = "no_match" });
        }

        var candidate = new ReconcileResult
        {
            MatchedGroups = candMatched,
            Unresolved = candUnresolved,
            Statistics = new ReconcileStatistics
            {
                TotalRecords = 1000,
                MatchedRecords = 880,
                UnresolvedRecords = 120,
                AmbiguousRecords = 0,
                MatchedGroups = 440
            }
        };

        var dummyRowsA = new List<Dictionary<string, string>>();
        var dummyRowsB = new List<Dictionary<string, string>>();
        for (int i = 0; i < 500; i++)
        {
            dummyRowsA.Add(new() { ["id"] = $"{i}" });
            dummyRowsB.Add(new() { ["id"] = $"{i}" });
        }

        var datasets = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["A"] = dummyRowsA,
            ["B"] = dummyRowsB
        };

        var impact = AnalysisImpact.Compute(baseline, candidate, datasets);

        Assert.Equal(1000, impact.TotalRecords);
        Assert.Equal(700, impact.BaseFullMatchRecords);
        Assert.Equal(100, impact.BasePartialMatchRecords);
        Assert.Equal(200, impact.BaseUnresolvedRecords);
        Assert.Equal(50, impact.BaseAmbiguousRecords);

        Assert.Equal(760, impact.CandidateFullMatchRecords);
        Assert.Equal(120, impact.CandidatePartialMatchRecords);
        Assert.Equal(120, impact.CandidateUnresolvedRecords);

        Assert.Equal(70.0, impact.BaseFullMatchPercentage);
        Assert.Equal(80.0, impact.BaseAnyMatchPercentage);

        Assert.Equal(76.0, impact.CandidateFullMatchPercentage);
        Assert.Equal(88.0, impact.CandidateAnyMatchPercentage);

        Assert.Equal(6.0, impact.FullImprovementPercentagePoints);
        Assert.Equal(8.0, impact.AnyImprovementPercentagePoints);
        Assert.Equal(40.0, impact.RecoveredUnresolvedPercentage);
        Assert.Equal(80, impact.RecoveredUnresolvedRecords);

        Assert.True(impact.Preservation.Passed);
        Assert.Equal(400, impact.Preservation.PreservedGroupsCount);
        Assert.Equal(800, impact.ProtectedMatchedRecords);
    }
}
