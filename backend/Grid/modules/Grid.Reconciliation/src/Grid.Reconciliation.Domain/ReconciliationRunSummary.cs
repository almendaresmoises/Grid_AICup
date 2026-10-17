using Volo.Abp.Domain.Entities;

namespace Grid.Reconciliation;

/// <summary>One compact, immutable counter row per completed run, without source records or JSON.</summary>
public class ReconciliationRunSummary : Entity<Guid>
{
    protected ReconciliationRunSummary() { }

    public Guid ProfileId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int TotalRecords { get; private set; }
    public int FullMatchRecords { get; private set; }
    public int PartialMatchRecords { get; private set; }
    public int ExceptionRecords { get; private set; }

    public static ReconciliationRunSummary From(ReconciliationRun run, ReconcileResult result)
    {
        var partial = result.MatchedGroups
            .Where(group => group.RuleType == "partial" || group.Evidence.Comparison == "partial_match")
            .Sum(group => group.Records.Sum(dataset => dataset.Value.Count));
        var full = result.Statistics.MatchedRecords - partial;
        var exceptions = result.Statistics.UnresolvedRecords;
        if (full < 0 || full + partial + exceptions != result.Statistics.TotalRecords)
            throw new ReconciliationException("Run outcome counts do not account for every input record.");
        return new ReconciliationRunSummary
        {
            Id = run.Id,
            ProfileId = run.ProfileId,
            // Retain the run's recorded wall-clock timestamp. Legacy runs do not store a timezone.
            CreatedAt = DateTime.SpecifyKind(run.CreationTime, DateTimeKind.Unspecified),
            TotalRecords = result.Statistics.TotalRecords,
            FullMatchRecords = full,
            PartialMatchRecords = partial,
            ExceptionRecords = exceptions,
        };
    }
}
