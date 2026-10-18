using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Grid.Reconciliation;

public class DatasetColumnDto
{
    public string Name { get; set; } = "";

    public string Type { get; set; } = "text";
}

public class DatasetDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public int RowCount { get; set; }

    public List<DatasetColumnDto> Columns { get; set; } = [];

    public List<Dictionary<string, string>>? SampleRows { get; set; }
}

public class CreateDatasetDto
{
    public string Code { get; set; } = "";

    public string? Name { get; set; }

    public string CsvText { get; set; } = "";
}

public class UpdateDatasetDto
{
    public List<DatasetColumnDto> Columns { get; set; } = [];
}

public class PackageDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public Guid ProfileId { get; set; }

    public string ProfileName { get; set; } = "";

    public int ProfileVersion { get; set; }

    public string ProfileStatus { get; set; } = "";

    public DateTime CreationTime { get; set; }

    public bool CreatedWithAi { get; set; }
}

public class CreatePackageDto
{
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public bool CreatedWithAi { get; set; }
}

public class ProfileDto
{
    public Guid? AiConfigurationId { get; set; }
    public Guid Id { get; set; }

    public Guid PackageId { get; set; }

    public string PackageName { get; set; } = "";

    public string Name { get; set; } = "";

    public int Version { get; set; }

    public string Status { get; set; } = "";

    public string DefinitionJson { get; set; } = "{}";

    public string? ApprovedDefinitionJson { get; set; }

    public bool HasBaseline { get; set; }

    public bool CreatedWithAi { get; set; }
}

public class SaveProfileDto
{
    public string DefinitionJson { get; set; } = "";

    public bool CreatedWithAi { get; set; }

}

public class RunDto
{
    public string ProfileName { get; set; } = "";

    public Guid? CreatorId { get; set; }
    public Guid Id { get; set; }

    public Guid ProfileId { get; set; }

    public int ProfileVersion { get; set; }

    public string Status { get; set; } = "";

    public DateTime CreationTime { get; set; }

    public int TotalRecords { get; set; }

    public int MatchedRecords { get; set; }

    public int UnresolvedRecords { get; set; }

    public int AmbiguousRecords { get; set; }

    public int MatchedGroups { get; set; }
}

public class MatchGroupDto
{
    public string Rule { get; set; } = "";

    public string RuleType { get; set; } = "exact";

    public List<string> Key { get; set; } = [];

    public Dictionary<string, List<int>> Records { get; set; } = [];

    public EvidenceDto Evidence { get; set; } = new();

    public string? Remarks { get; set; }

    public decimal? Score { get; set; }
}

public class EvidenceDto
{
    public string Comparison { get; set; } = "";

    public string? Target { get; set; }

    public string? SourceSum { get; set; }

    public string? Variance { get; set; }

    public string? Remarks { get; set; }

    public decimal? Score { get; set; }

    [JsonPropertyName("divergent_key")]
    public string? DivergentKey { get; set; }
}

public class UnresolvedRowDto
{
    public string Dataset { get; set; } = "";

    public int CsvRow { get; set; }

    public string Reason { get; set; } = "";

    public Dictionary<string, string> Record { get; set; } = [];
}

public class RunDetailDto : RunDto
{
    public Dictionary<string, List<Dictionary<string, string>>> Datasets { get; set; } = [];
    public List<MatchGroupDto> Groups { get; set; } = [];

    public List<UnresolvedRowDto> Unresolved { get; set; } = [];
}

public class CreateRunDto
{
    public Guid ProfileId { get; set; }

    public Dictionary<string, Guid> ParserRunIds { get; set; } = [];

    public Dictionary<string, List<Guid>>? MultiParserRunIds { get; set; }
}

public class RunListInput
{
    public Guid? ProfileId { get; set; }
}

public class ProposeProfileDto
{
    public List<string> DatasetCodes { get; set; } = [];

    public string? Context { get; set; }

    public Dictionary<string, Guid> ParserRunIds { get; set; } = [];

    public Dictionary<string, List<Guid>> MultiParserRunIds { get; set; } = [];

    public Dictionary<string, string> CsvSamples { get; set; } = [];
}

public class ProposalDto
{
    public string DefinitionJson { get; set; } = "";

    public string Source { get; set; } = "";

    public List<string> Warnings { get; set; } = [];
}

public class InvestigateInput
{
    public Guid RunId { get; set; }

    public string? Context { get; set; }
}

public class InvestigationDto
{
    public string Diagnosis { get; set; } = "";

    public List<string> Hypotheses { get; set; } = [];

    public string ProposedDefinitionJson { get; set; } = "";

    public List<string> ChangeSummary { get; set; } = [];

    public List<string> Risks { get; set; } = [];
}

public class RegressionPreviewInput
{
    public Guid ProfileId { get; set; }

    public Guid? RunId { get; set; }

    public string ProposedDefinitionJson { get; set; } = "";
}

public class StatisticsDto
{
    public int TotalRecords { get; set; }

    public int MatchedRecords { get; set; }

    public int UnresolvedRecords { get; set; }

    public int AmbiguousRecords { get; set; }

    public int MatchedGroups { get; set; }
}

public class RegressionPreviewDto
{
    public bool Passed { get; set; }

    public bool HasBaseline { get; set; }

    public string Message { get; set; } = "";

    public StatisticsDto? CurrentProfile { get; set; }

    public StatisticsDto? ProposedOnCurrent { get; set; }

    public StatisticsDto? BaselineProfile { get; set; }

    public StatisticsDto? ProposedOnBaseline { get; set; }
}

public class ProposeParsersDto
{
    public List<FileSnippetDto> Files { get; set; } = [];

    public string? Context { get; set; }
}

public class FileSnippetDto
{
    public string Code { get; set; } = "";

    public string FileName { get; set; } = "";

    public string RawSnippet { get; set; } = "";
}

public class ParsersProposalDto
{
    public List<ParserProposalItemDto> Parsers { get; set; } = [];

    public string Source { get; set; } = "";

    public List<string> Warnings { get; set; } = [];
}

public class ParserProposalItemDto
{
    public string Code { get; set; } = "";

    public string Delimiter { get; set; } = ",";

    public string HeaderMode { get; set; } = "firstRow";

    public int HeaderRowIndex { get; set; } = 0;

    public int SkipRows { get; set; } = 0;

    public bool RemoveTotalRows { get; set; }

    public string TotalRowKeywords { get; set; } = "";

    public List<ParserStepProposalDto> Steps { get; set; } = [];
}

public class ParserStepProposalDto
{
    public string Id { get; set; } = "";

    public string Kind { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Label { get; set; } = "";

    public string Source { get; set; } = "";

    public Dictionary<string, string> Params { get; set; } = [];
}

public class AiFallbackRequestDto
{
    public int StepIndex { get; set; }

    public string StepName { get; set; } = "";

    public string ErrorMessage { get; set; } = "";

    public List<FileAnomalyDto> AnomalousFiles { get; set; } = [];

    public string? Context { get; set; }
}

public class FileAnomalyDto
{
    public string FileId { get; set; } = "";

    public string FileName { get; set; } = "";

    public string DatasetCode { get; set; } = "";

    public string ParserCode { get; set; } = "";

    public long FileSize { get; set; }

    public string RawSnippet { get; set; } = "";

    public string ErrorDetail { get; set; } = "";
}

public class AiFallbackDecisionDto
{
    public bool CanRecover { get; set; }

    public string Diagnosis { get; set; } = "";

    public string ActionTaken { get; set; } = "";

    public string UserMessage { get; set; } = "";

    public List<string> FilesToSkip { get; set; } = [];

    public List<ParserProposalItemDto> AdjustedParsers { get; set; } = [];
}
