using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;

namespace Grid.Reconciliation;

public class ConfigurationCommit : Entity<Guid>
{
    public ConfigurationCommit() { }
    public ConfigurationCommit(Guid id) : base(id) { }

    public Guid ProfileId { get; set; }
    public int ProfileVersion { get; set; }
    public string ApprovedProfileSnapshotJson { get; set; } = "{}";
    public string SelectedOperationIdsJson { get; set; } = "[]";
    public string RejectedOperationIdsJson { get; set; } = "[]";
    public Guid? ParentCommitId { get; set; }
    public string BaseHash { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string RevisionBindingsJson { get; set; } = "{}";
    public string SourceManifestJson { get; set; } = "{}";
    public string ValidationReportHash { get; set; } = "";
    public Guid? ActorId { get; set; }
    public Guid DecisionRequestId { get; set; }
    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}

public class ConfigurationExecution : CreationAuditedAggregateRoot<Guid>
{
    public ConfigurationExecution() { }
    public ConfigurationExecution(Guid id) : base(id) { }

    public Guid CommitId { get; set; }
    public Guid ExecutionRequestId { get; set; }
    public string AssessedInputManifestHash { get; set; } = "";
    public string Status { get; set; } = "Queued"; // Queued, PreparingInputs, Reconciling, Succeeded, Failed
    public string Stage { get; set; } = "Queued";
    public Guid? ResultingRunId { get; set; }
    public string? ErrorMessage { get; set; }
    public int Attempt { get; set; } = 1;
    public DateTime? CompletionTime { get; set; }
}
