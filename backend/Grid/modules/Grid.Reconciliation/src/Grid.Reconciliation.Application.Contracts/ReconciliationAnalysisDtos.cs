using System;
using System.Collections.Generic;

namespace Grid.Reconciliation;

public class StartOrResumeAnalysisDto
{
    public Guid RequestId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid? SourceRunId { get; set; }
    public string? Context { get; set; }
    public string? ExpectedProfileRevision { get; set; }
}

public class StartOrResumeAnalysisResultDto
{
    public Guid AnalysisId { get; set; }
    public bool Reused { get; set; }
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public string Revision { get; set; } = "";
}

public class DiagnosisItemDto
{
    public string Category { get; set; } = "";
    public string Summary { get; set; } = "";
    public int AffectedRecordCount { get; set; }
    public List<string> SampleEvidenceIds { get; set; } = [];
}

public class AnalysisChangeDto
{
    public Guid Id { get; set; }
    public string DependencyGroupId { get; set; } = "";
    public string ChangeType { get; set; } = "";
    public string TargetIdentifier { get; set; } = "";
    public int? TargetObjectVersion { get; set; }
    public string OperationType { get; set; } = "";
    public string OperationJson { get; set; } = "{}";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
    public string Rationale { get; set; } = "";
    public List<string> EvidenceIds { get; set; } = [];
    public string SafetyClass { get; set; } = "safe_additive";
    public bool IsSelectable { get; set; } = true;
    public string? BlockerReason { get; set; }
}

public class RegressionVerdictDto
{
    public Guid RunId { get; set; }
    public string Description { get; set; } = "";
    public bool Passed { get; set; }
    public string Reason { get; set; } = "";
    public int TotalRecords { get; set; }
    public int MatchedRecords { get; set; }
}

public class DatasetBreakdownDto
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

public class GroupContributionDto
{
    public string GroupId { get; set; } = "";
    public int NewlyMatchedRecords { get; set; }
    public int NewlyMatchedGroups { get; set; }
    public int FullMatches { get; set; }
    public int PartialMatches { get; set; }
    public double IndependentAnyGainPoints { get; set; }
    public double MarginalAnyGainPoints { get; set; }
}

public class AnalysisImpactSummaryDto
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

    public double BaseFullMatchPercentage { get; set; }
    public double BasePartialMatchPercentage { get; set; }
    public double BaseAnyMatchPercentage { get; set; }

    public double CandidateFullMatchPercentage { get; set; }
    public double CandidatePartialMatchPercentage { get; set; }
    public double CandidateAnyMatchPercentage { get; set; }

    public double FullImprovementPercentagePoints { get; set; }
    public double AnyImprovementPercentagePoints { get; set; }
    public double RecoveredUnresolvedPercentage { get; set; }

    public bool PreservedMatches { get; set; }
    public int PreservedGroupsCount { get; set; }
    public int LostGroupsCount { get; set; }
    public int DegradedGroupsCount { get; set; }
    public int ChangedMembersGroupsCount { get; set; }
    public List<string> PreservationViolations { get; set; } = [];

    public string Coverage { get; set; } = "FullReplay";
    public List<RegressionVerdictDto> RegressionVerdicts { get; set; } = [];
    public List<DatasetBreakdownDto> DatasetBreakdowns { get; set; } = [];
    public List<GroupContributionDto> GroupContributions { get; set; } = [];
}

public class ReconciliationAnalysisDetailDto
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = "";
    public int ProfileVersion { get; set; }
    public Guid SourceRunId { get; set; }
    public DateTime? SourceRunTime { get; set; }
    public int SourceRunTotalRecords { get; set; }
    public int SourceRunMatchedRecords { get; set; }
    public int SourceRunUnresolvedRecords { get; set; }
    public List<string> Datasets { get; set; } = [];

    public string Status { get; set; } = "";
    public string Stage { get; set; } = "";
    public DateTime CreationTime { get; set; }
    public DateTime? CompletionTime { get; set; }
    public string Revision { get; set; } = "";
    public string? Context { get; set; }

    public List<string> SelectedGroupIds { get; set; } = [];
    public string SelectionHash { get; set; } = "";
    public AnalysisImpactSummaryDto? Impact { get; set; }
    public List<DiagnosisItemDto> Diagnoses { get; set; } = [];

    public Guid? ConfigurationCommitId { get; set; }
    public int? CommittedProfileVersion { get; set; }
    public Guid? ExecutionId { get; set; }
    public string? ExecutionStatus { get; set; }
    public Guid? ResultingRunId { get; set; }

    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public bool CanAccept { get; set; }
    public bool CanReject { get; set; }
    public bool CanRevalidate { get; set; }
    public bool CanRetry { get; set; }
    public bool CanRun { get; set; }
    public string? BlockerReason { get; set; }
}

public class BatchProfileStatusesInputDto
{
    public List<Guid> ProfileIds { get; set; } = [];
}

public class ProfileAnalysisSummaryDto
{
    public Guid ProfileId { get; set; }
    public Guid? ActiveAnalysisId { get; set; }
    public string? ActiveStatus { get; set; }
    public string? ActiveStage { get; set; }
    public Guid? LastAnalysisId { get; set; }
    public string? LastStatus { get; set; }
    public Guid? ActiveCommitId { get; set; }
    public int? ActiveCommitVersion { get; set; }
    public Guid? LatestRunId { get; set; }
    public bool HasSuccessfulRun { get; set; }
    public string ActionLabel { get; set; } = "Analyze";
    public string ActionType { get; set; } = "start"; // "start" | "reopen" | "review" | "view_accepted" | "view_run" | "view_history" | "no_run"
    public string? Explanation { get; set; }
}

public class UpdateAnalysisSelectionDto
{
    public string ExpectedRevision { get; set; } = "";
    public List<string> SelectedGroupIds { get; set; } = [];
}

public class ReconciliationAnalysisDecisionDto
{
    public Guid RequestId { get; set; }
    public string ExpectedRevision { get; set; } = "";
    public string Decision { get; set; } = ""; // "AcceptSelected" | "RejectAll"
    public string SelectionHash { get; set; } = "";
    public Guid? ValidationId { get; set; }
    public string? ValidationHash { get; set; }
    public List<string>? MaterialAcknowledgements { get; set; }
}

public class DecisionResultDto
{
    public Guid AnalysisId { get; set; }
    public string Decision { get; set; } = "";
    public string Status { get; set; } = "";
    public Guid? CommitId { get; set; }
    public int? CommittedProfileVersion { get; set; }
}

public class ExecuteAnalysisInputDto
{
    public Guid RequestId { get; set; }
    public Guid CommitId { get; set; }
    public string InputMode { get; set; } = "AssessedInputs";
    public string? ExpectedCommitHash { get; set; }
}

public class ConfigurationExecutionDto
{
    public Guid ExecutionId { get; set; }
    public Guid CommitId { get; set; }
    public string Status { get; set; } = "";
    public string Stage { get; set; } = "";
    public Guid? ResultingRunId { get; set; }
    public string? ErrorMessage { get; set; }
    public int Attempt { get; set; }
}
