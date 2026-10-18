using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Grid.Reconciliation;

public interface IReconciliationAnalysisAppService : IApplicationService
{
    Task<StartOrResumeAnalysisResultDto> StartOrResumeAsync(StartOrResumeAnalysisDto input);
    Task<ReconciliationAnalysisDetailDto> GetAsync(Guid id);
    Task<List<AnalysisChangeDto>> GetChangesAsync(Guid id);
    Task<List<ProfileAnalysisSummaryDto>> GetProfileStatusesAsync(BatchProfileStatusesInputDto input);
    Task<List<ReconciliationAnalysisDetailDto>> GetHistoryAsync(Guid profileId);
    Task<ReconciliationAnalysisDetailDto> UpdateSelectionAsync(Guid id, UpdateAnalysisSelectionDto input);
    Task<ReconciliationAnalysisDetailDto> RevalidateAsync(Guid id);
    Task<DecisionResultDto> MakeDecisionAsync(Guid id, ReconciliationAnalysisDecisionDto input);
    Task<ReconciliationAnalysisDetailDto> RetryAsync(Guid id);
    Task<ConfigurationExecutionDto> ExecuteAsync(Guid id, ExecuteAnalysisInputDto input);
    Task<ConfigurationExecutionDto> GetExecutionAsync(Guid id);
}
