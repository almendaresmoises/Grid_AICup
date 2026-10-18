using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;

namespace Grid.Reconciliation;

public static class AnalysisStatuses
{
    public const string Queued = "Queued";
    public const string Analyzing = "Analyzing";
    public const string Validating = "Validating";
    public const string AwaitingDecision = "AwaitingDecision";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string NoRecommendations = "NoRecommendations";
    public const string Failed = "Failed";
    public const string Stale = "Stale";
    public const string Blocked = "Blocked";
}

public class ReconciliationAnalysis : CreationAuditedAggregateRoot<Guid>
{
    public ReconciliationAnalysis() { }
    public ReconciliationAnalysis(Guid id) : base(id) { }

    public Guid ProfileId { get; set; }
    public Guid SourceRunId { get; set; }
    public string Status { get; set; } = AnalysisStatuses.Queued;
    public string Stage { get; set; } = "Initializing";
    public DateTime? CompletionTime { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string? Context { get; set; }
    public string BaseHash { get; set; } = "";
    public string ManifestHash { get; set; } = "";
    public string ManifestJson { get; set; } = "{}";
    public string? PromptModelProvenanceJson { get; set; }
    public string SelectionJson { get; set; } = "[]";
    public string SelectionHash { get; set; } = "";
    public Guid? PreviewId { get; set; }
    public Guid? DecisionActorId { get; set; }
    public DateTime? DecisionTime { get; set; }
    public Guid? DecisionRequestId { get; set; }
    public Guid? ConfigurationCommitId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
}

public class ProfileAnalysisSlot : Entity<Guid>
{
    public ProfileAnalysisSlot() { }
    public ProfileAnalysisSlot(Guid profileId) : base(profileId) { }

    public Guid? ActiveAnalysisId { get; set; }
    public Guid? LastAnalysisId { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
}

public class ReconciliationAnalysisChange : Entity<Guid>
{
    public ReconciliationAnalysisChange() { }
    public ReconciliationAnalysisChange(Guid id) : base(id) { }

    public Guid AnalysisId { get; set; }
    public string DependencyGroupId { get; set; } = "";
    public string ChangeType { get; set; } = ""; // "profile" | "parser" | "dataset"
    public string TargetIdentifier { get; set; } = "";
    public string BaseHash { get; set; } = "";
    public int? TargetObjectVersion { get; set; }
    public string OperationType { get; set; } = ""; // "append_rule", "add_parser_field", "add_dataset_field", etc.
    public string OperationJson { get; set; } = "{}";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
    public string Rationale { get; set; } = "";
    public string EvidenceIdsJson { get; set; } = "[]";
    public string SafetyClass { get; set; } = "safe_additive"; // "safe_additive" | "material_change" | "unsupported"
    public bool IsSelectable { get; set; } = true;
    public string? BlockerReason { get; set; }
}

public class ReconciliationAnalysisValidation : Entity<Guid>
{
    public ReconciliationAnalysisValidation() { }
    public ReconciliationAnalysisValidation(Guid id) : base(id) { }

    public Guid AnalysisId { get; set; }
    public string SelectionHash { get; set; } = "";
    public string BaseHash { get; set; } = "";
    public string InputManifestHash { get; set; } = "";
    public string EngineVersion { get; set; } = "v1";
    public string State { get; set; } = "Completed"; // "Pending" | "Completed" | "Failed"
    public string Coverage { get; set; } = "FullReplay"; // "FullReplay" | "PartialEvidence" | "Unavailable"
    public int FullMatchCount { get; set; }
    public int PartialMatchCount { get; set; }
    public int UnresolvedCount { get; set; }
    public int AmbiguousCount { get; set; }
    public int BaseFullMatchCount { get; set; }
    public int BasePartialMatchCount { get; set; }
    public int BaseUnresolvedCount { get; set; }
    public int BaseAmbiguousCount { get; set; }
    public int TotalRecords { get; set; }
    public bool PreservedMatches { get; set; }
    public string RegressionVerdictsJson { get; set; } = "[]";
    public string DetailedReportJson { get; set; } = "{}";
    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
    public DateTime? CompletionTime { get; set; }
}

public class AnalysisWorkAttempt : Entity<Guid>
{
    public AnalysisWorkAttempt() { }
    public AnalysisWorkAttempt(Guid id) : base(id) { }

    public Guid? AnalysisId { get; set; }
    public Guid? ExecutionId { get; set; }
    public string WorkType { get; set; } = ""; // "Analysis" | "Validation" | "Execution"
    public string Stage { get; set; } = "";
    public int AttemptNumber { get; set; } = 1;
    public string LeaseOwner { get; set; } = "";
    public DateTime LeaseExpiry { get; set; }
    public DateTime Heartbeat { get; set; }
    public string? CheckpointJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ConfigurationDispatch : Entity<Guid>
{
    public ConfigurationDispatch() { }
    public ConfigurationDispatch(Guid id) : base(id) { }

    public string Kind { get; set; } = ""; // "Analyze" | "Validate" | "Execute"
    public string PayloadJson { get; set; } = "{}";
    public string DeduplicationKey { get; set; } = "";
    public int Attempt { get; set; } = 0;
    public DateTime NextAttemptTime { get; set; } = DateTime.UtcNow;
    public DateTime? SentTime { get; set; }
    public string Status { get; set; } = "Pending"; // "Pending" | "Sent" | "Failed"
}

public class ConfigurationRequestReceipt : Entity<Guid>
{
    public ConfigurationRequestReceipt() { }
    public ConfigurationRequestReceipt(Guid id) : base(id) { }

    public Guid ActorId { get; set; }
    public string OperationKind { get; set; } = ""; // "StartOrResume" | "Decision" | "Run"
    public Guid RequestId { get; set; }
    public string RequestBodyHash { get; set; } = "";
    public Guid TargetProfileId { get; set; }
    public Guid? TargetAnalysisId { get; set; }
    public Guid ResultId { get; set; }
    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
