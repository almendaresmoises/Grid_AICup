using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Grid.Reconciliation;

public interface IAiConfigurationAppService : Volo.Abp.Application.Services.IApplicationService
{
    Task<AiConfigurationDto> CreateAsync(StartAiConfigurationDto input);
    Task<AiConfigurationDto> GetAsync(Guid id);
    Task<List<AiConfigurationDto>> GetListAsync();
    Task<ConfigurationValidationDto> GetValidationAsync(Guid id);
    Task<RunDetailDto> RunAsync(Guid id, RunValidatedConfigurationDto input);
}

public class ConfigurationFileDto
{
    public string DatasetCode { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ParserCode { get; set; } = "";
    public Guid ParserId { get; set; }
    public Guid RunId { get; set; }
}

public class StartAiConfigurationDto
{
    public Guid RequestId { get; set; }
    public string Context { get; set; } = "";
    public List<ConfigurationFileDto> Files { get; set; } = [];
}

public class AiConfigurationDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "";
    public string? Error { get; set; }
    public PackageDto? Package { get; set; }
    public string Source { get; set; } = "";
    public List<ConfigurationFileDto> Files { get; set; } = [];
    public ConfigurationValidationDto? Validation { get; set; }
}

public class ConfigurationValidationDto
{
    public bool Passed { get; set; }
    public string Revision { get; set; } = "";
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<ConfigurationColumnDto> Columns { get; set; } = [];
    public List<ConfigurationPassDto> Passes { get; set; } = [];
    public long TotalRecords { get; set; }
    public long FullMatchRecords { get; set; }
    public long PartialMatchRecords { get; set; }
    public long ExceptionRecords { get; set; }
}

public class ConfigurationColumnDto
{
    public string Dataset { get; set; } = "";
    public string Column { get; set; } = "";
    public int Missing { get; set; }
    public int Distinct { get; set; }
    public int Total { get; set; }
}

public class ConfigurationPassDto
{
    public string Rule { get; set; } = "";
    public int Groups { get; set; }
    public int Records { get; set; }
}

public class RunValidatedConfigurationDto
{
    public string Revision { get; set; } = "";
}
