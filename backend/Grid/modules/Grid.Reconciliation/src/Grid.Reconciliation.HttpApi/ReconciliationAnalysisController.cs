using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace Grid.Reconciliation;

[Authorize]
[Route("api/app/reconciliation-analysis")]
public class ReconciliationAnalysisController : AbpControllerBase, IReconciliationAnalysisAppService
{
    private readonly IReconciliationAnalysisAppService _analysisService;

    public ReconciliationAnalysisController(IReconciliationAnalysisAppService analysisService)
    {
        _analysisService = analysisService;
    }

    [HttpPost("start-or-resume")]
    public Task<StartOrResumeAnalysisResultDto> StartOrResumeAsync([FromBody] StartOrResumeAnalysisDto input)
    {
        return _analysisService.StartOrResumeAsync(input);
    }

    [HttpGet("{id}")]
    public Task<ReconciliationAnalysisDetailDto> GetAsync(Guid id)
    {
        return _analysisService.GetAsync(id);
    }

    [HttpGet("{id}/changes")]
    public Task<List<AnalysisChangeDto>> GetChangesAsync(Guid id)
    {
        return _analysisService.GetChangesAsync(id);
    }

    [HttpPost("profile-statuses")]
    public Task<List<ProfileAnalysisSummaryDto>> GetProfileStatusesAsync([FromBody] BatchProfileStatusesInputDto input)
    {
        return _analysisService.GetProfileStatusesAsync(input);
    }

    [HttpGet("history")]
    public Task<List<ReconciliationAnalysisDetailDto>> GetHistoryAsync([FromQuery] Guid profileId)
    {
        return _analysisService.GetHistoryAsync(profileId);
    }

    [HttpPut("{id}/selection")]
    public Task<ReconciliationAnalysisDetailDto> UpdateSelectionAsync(Guid id, [FromBody] UpdateAnalysisSelectionDto input)
    {
        return _analysisService.UpdateSelectionAsync(id, input);
    }

    [HttpPost("{id}/revalidate")]
    public Task<ReconciliationAnalysisDetailDto> RevalidateAsync(Guid id)
    {
        return _analysisService.RevalidateAsync(id);
    }

    [HttpPost("{id}/decision")]
    public Task<DecisionResultDto> MakeDecisionAsync(Guid id, [FromBody] ReconciliationAnalysisDecisionDto input)
    {
        return _analysisService.MakeDecisionAsync(id, input);
    }

    [HttpPost("{id}/retry")]
    public Task<ReconciliationAnalysisDetailDto> RetryAsync(Guid id)
    {
        return _analysisService.RetryAsync(id);
    }

    [HttpPost("{id}/run")]
    public Task<ConfigurationExecutionDto> ExecuteAsync(Guid id, [FromBody] ExecuteAnalysisInputDto input)
    {
        return _analysisService.ExecuteAsync(id, input);
    }

    [HttpGet("{id}/execution")]
    public Task<ConfigurationExecutionDto> GetExecutionAsync(Guid id)
    {
        return _analysisService.GetExecutionAsync(id);
    }
}
