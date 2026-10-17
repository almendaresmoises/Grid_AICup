using System;
using System.Collections.Generic;
using Grid.Reconciliation;
using Xunit;

namespace Grid;

public class ReconciliationRunSummaryTests
{
    [Fact]
    public void Counts_partial_records_across_all_participating_datasets_and_keeps_exceptions_separate()
    {
        var run = new ReconciliationRun(Guid.NewGuid()) { ProfileId = Guid.NewGuid(), CreationTime = new DateTime(2026, 10, 17, 12, 0, 0) };
        var result = new ReconcileResult
        {
            Statistics = new ReconcileStatistics { TotalRecords = 12, MatchedRecords = 9, UnresolvedRecords = 3, AmbiguousRecords = 2 },
            MatchedGroups = [new MatchGroupResult
            {
                RuleType = "partial",
                Records = new Dictionary<string, List<int>> { ["A"] = [2, 3], ["B"] = [2], ["C"] = [2] },
                Evidence = new EvidenceResult { Comparison = "partial_match" },
            }],
        };
        var summary = ReconciliationRunSummary.From(run, result);
        Assert.Equal(run.Id, summary.Id);
        Assert.Equal(run.ProfileId, summary.ProfileId);
        Assert.Equal(5, summary.FullMatchRecords);
        Assert.Equal(4, summary.PartialMatchRecords);
        Assert.Equal(3, summary.ExceptionRecords); // Ambiguous records are already part of unresolved, not added twice.
        Assert.Equal(12, summary.TotalRecords);
    }

    [Fact]
    public void Rejects_outcomes_that_do_not_account_for_every_record()
    {
        var result = new ReconcileResult { Statistics = new ReconcileStatistics { TotalRecords = 10, MatchedRecords = 4, UnresolvedRecords = 5 } };
        Assert.Throws<ReconciliationException>(() => ReconciliationRunSummary.From(new ReconciliationRun(Guid.NewGuid()), result));
    }
}
