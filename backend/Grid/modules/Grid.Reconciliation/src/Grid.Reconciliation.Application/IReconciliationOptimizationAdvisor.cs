using System.Collections.Generic;
using System.Threading.Tasks;

namespace Grid.Reconciliation;

public class OptimizationAdvisorInput
{
    public ProfileDefinition BaseProfile { get; set; } = new();
    public Dictionary<string, List<Dictionary<string, string>>> Rows { get; set; } = [];
    public AnalysisEvidenceManifest Manifest { get; set; } = new();
    public string? Context { get; set; }
}

public class ProposedChangeGroup
{
    public string GroupId { get; set; } = "";
    public string Title { get; set; } = "";
    public string SafetyClass { get; set; } = "safe_additive"; // "safe_additive" | "material_change" | "unsupported"
    public List<ProposedOperation> Operations { get; set; } = [];
}

public class ProposedOperation
{
    public string OperationId { get; set; } = "";
    public string DependencyGroupId { get; set; } = "";
    public string ChangeType { get; set; } = "profile"; // "profile" | "parser" | "dataset"
    public string TargetIdentifier { get; set; } = "";
    public int? TargetObjectVersion { get; set; }
    public string OperationType { get; set; } = "append_rule"; // "append_rule" | "add_parser_field" | "add_dataset_field" | "modify_rule"
    public string OperationJson { get; set; } = "{}";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
    public string Rationale { get; set; } = "";
    public List<string> EvidenceIds { get; set; } = [];
    public string SafetyClass { get; set; } = "safe_additive";
    public bool IsSelectable { get; set; } = true;
    public string? BlockerReason { get; set; }
}

public class OptimizationAdvisorResult
{
    public string Source { get; set; } = "model";
    public List<DiagnosisItemDto> Diagnoses { get; set; } = [];
    public List<ProposedChangeGroup> ChangeGroups { get; set; } = [];
    public string? PromptModelProvenanceJson { get; set; }
}

public interface IReconciliationOptimizationAdvisor
{
    Task<OptimizationAdvisorResult> AdviseAsync(OptimizationAdvisorInput input);
}
