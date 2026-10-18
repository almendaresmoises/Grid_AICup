using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace Grid.Reconciliation;

public class ConfigurationExecutionJobArgs
{
    public Guid ExecutionId { get; set; }
}

public class ConfigurationExecutionJob : AsyncBackgroundJob<ConfigurationExecutionJobArgs>, ITransientDependency
{
    private readonly IRepository<ConfigurationExecution, Guid> _executions;
    private readonly IRepository<ConfigurationCommit, Guid> _commits;
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IRepository<ReconciliationAnalysis, Guid> _analyses;
    private readonly IRepository<ReconciliationRun, Guid> _runs;
    private readonly ReconciliationExecutionService _executionService;
    private readonly IUnitOfWorkManager _uow;
    private readonly ILogger<ConfigurationExecutionJob> _logger;

    public ConfigurationExecutionJob(
        IRepository<ConfigurationExecution, Guid> executions,
        IRepository<ConfigurationCommit, Guid> commits,
        IRepository<Profile, Guid> profiles,
        IRepository<ReconciliationAnalysis, Guid> analyses,
        IRepository<ReconciliationRun, Guid> runs,
        ReconciliationExecutionService executionService,
        IUnitOfWorkManager uow,
        ILogger<ConfigurationExecutionJob> logger)
    {
        _executions = executions;
        _commits = commits;
        _profiles = profiles;
        _analyses = analyses;
        _runs = runs;
        _executionService = executionService;
        _uow = uow;
        _logger = logger;
    }

    public override async Task ExecuteAsync(ConfigurationExecutionJobArgs args)
    {
        using var unit = _uow.Begin(requiresNew: true, isTransactional: true);
        var execution = await _executions.FindAsync(args.ExecutionId);
        if (execution == null || execution.Status == "Succeeded") return;

        try
        {
            execution.Status = "PreparingInputs";
            execution.Stage = "PreparingInputs";
            await _executions.UpdateAsync(execution, autoSave: true);

            var commit = await _commits.GetAsync(execution.CommitId);
            var profile = await _profiles.GetAsync(commit.ProfileId);

            // Find source run for assessed inputs
            var analysis = await _analyses.FirstOrDefaultAsync(a => a.ConfigurationCommitId == commit.Id);
            if (analysis == null)
            {
                throw new InvalidOperationException($"Could not find reconciliation analysis associated with commit {commit.Id}");
            }

            var sourceRun = await _runs.GetAsync(analysis.SourceRunId);
            var rows = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(sourceRun.DatasetSnapshotJson)
                ?? throw new InvalidOperationException("Source run does not contain dataset snapshot.");

            execution.Status = "Reconciling";
            execution.Stage = "Reconciling";
            await _executions.UpdateAsync(execution, autoSave: true);

            var run = await _executionService.ExecuteRunAsync(
                profile,
                commit.ApprovedProfileSnapshotJson,
                rows,
                commitId: commit.Id,
                analysisId: analysis.Id,
                executionId: execution.Id);

            execution.ResultingRunId = run.Id;
            execution.Status = "Succeeded";
            execution.Stage = "Succeeded";
            execution.CompletionTime = DateTime.UtcNow;
            await _executions.UpdateAsync(execution, autoSave: true);

            await unit.CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execution failed for execution {Id}", execution.Id);
            execution.Status = "Failed";
            execution.Stage = "Failed";
            execution.ErrorMessage = ex.Message;
            execution.CompletionTime = DateTime.UtcNow;
            await _executions.UpdateAsync(execution, autoSave: true);
            await unit.CompleteAsync();
        }
    }
}
