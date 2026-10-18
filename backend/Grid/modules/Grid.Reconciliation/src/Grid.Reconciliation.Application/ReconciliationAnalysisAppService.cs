using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Grid.Reconciliation;

[Authorize]
[RemoteService(false)]
public class ReconciliationAnalysisAppService : ApplicationService, IReconciliationAnalysisAppService
{
    private readonly IRepository<ReconciliationAnalysis, Guid> _analyses;
    private readonly IRepository<ProfileAnalysisSlot, Guid> _slots;
    private readonly IRepository<ReconciliationAnalysisChange, Guid> _changes;
    private readonly IRepository<ReconciliationAnalysisValidation, Guid> _validations;
    private readonly IRepository<ConfigurationCommit, Guid> _commits;
    private readonly IRepository<ConfigurationExecution, Guid> _executions;
    private readonly IRepository<ConfigurationRequestReceipt, Guid> _receipts;
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IRepository<ReconciliationRun, Guid> _runs;
    private readonly IBackgroundJobManager _background;
    private readonly AnalysisReplayService _replay;
    private readonly ConfigurationCommitService _commitService;

    public ReconciliationAnalysisAppService(
        IRepository<ReconciliationAnalysis, Guid> analyses,
        IRepository<ProfileAnalysisSlot, Guid> slots,
        IRepository<ReconciliationAnalysisChange, Guid> changes,
        IRepository<ReconciliationAnalysisValidation, Guid> validations,
        IRepository<ConfigurationCommit, Guid> commits,
        IRepository<ConfigurationExecution, Guid> executions,
        IRepository<ConfigurationRequestReceipt, Guid> receipts,
        IRepository<Profile, Guid> profiles,
        IRepository<ReconciliationRun, Guid> runs,
        IBackgroundJobManager background,
        AnalysisReplayService replay,
        ConfigurationCommitService commitService)
    {
        _analyses = analyses;
        _slots = slots;
        _changes = changes;
        _validations = validations;
        _commits = commits;
        _executions = executions;
        _receipts = receipts;
        _profiles = profiles;
        _runs = runs;
        _background = background;
        _replay = replay;
        _commitService = commitService;
    }

    private Guid GetCurrentUserId()
    {
        try
        {
            return LazyServiceProvider != null && CurrentUser?.Id != null ? CurrentUser.GetId() : Guid.Empty;
        }
        catch
        {
            return Guid.Empty;
        }
    }

    private async Task<T?> FirstOrDefaultSafeAsync<T>(IQueryable<T> query)
    {
        try
        {
            if (LazyServiceProvider != null && AsyncExecuter != null)
            {
                return await AsyncExecuter.FirstOrDefaultAsync(query);
            }
        }
        catch
        {
            // fallback
        }
        return query.FirstOrDefault();
    }

    private async Task<List<T>> ToListSafeAsync<T>(IQueryable<T> query)
    {
        try
        {
            if (LazyServiceProvider != null && AsyncExecuter != null)
            {
                return await AsyncExecuter.ToListAsync(query);
            }
        }
        catch
        {
            // fallback
        }
        return query.ToList();
    }

    public async Task<StartOrResumeAnalysisResultDto> StartOrResumeAsync(StartOrResumeAnalysisDto input)
    {
        var actorId = GetCurrentUserId();
        if (input.RequestId == Guid.Empty) throw new UserFriendlyException("A valid RequestId is required.");

        var requestBodyHash = ComputeHash(JsonSerializer.Serialize(new { input.ProfileId, input.SourceRunId, input.Context }));

        // Check request receipt idempotency
        var existingReceipt = await _receipts.FirstOrDefaultAsync(r =>
            r.ActorId == actorId && r.OperationKind == "StartOrResume" && r.RequestId == input.RequestId);

        if (existingReceipt != null)
        {
            if (existingReceipt.RequestBodyHash != requestBodyHash)
            {
                throw new UserFriendlyException("RequestId already used with a different request payload.");
            }

            var priorAnalysis = await _analyses.GetAsync(existingReceipt.ResultId);
            return new StartOrResumeAnalysisResultDto
            {
                AnalysisId = priorAnalysis.Id,
                Reused = true,
                Status = priorAnalysis.Status,
                Revision = priorAnalysis.BaseHash,
                Message = "Resumed earlier assessment for this request ID.",
            };
        }

        // Check slot for profile
        var slot = await _slots.FindAsync(input.ProfileId);
        if (slot == null)
        {
            slot = new ProfileAnalysisSlot(input.ProfileId);
            await _slots.InsertAsync(slot, autoSave: true);
        }

        if (slot.ActiveAnalysisId.HasValue)
        {
            var active = await _analyses.FindAsync(slot.ActiveAnalysisId.Value);
            if (active != null && active.Status is AnalysisStatuses.Queued or AnalysisStatuses.Analyzing or AnalysisStatuses.Validating or AnalysisStatuses.AwaitingDecision or AnalysisStatuses.Stale or AnalysisStatuses.Blocked)
            {
                // Return active analysis
                return new StartOrResumeAnalysisResultDto
                {
                    AnalysisId = active.Id,
                    Reused = true,
                    Status = active.Status,
                    Revision = active.BaseHash,
                    Message = "An existing assessment for this profile is currently active or awaiting decision. Reopening existing analysis.",
                };
            }
        }

        // Validate source run
        ReconciliationRun sourceRun;
        if (input.SourceRunId.HasValue)
        {
            sourceRun = await _runs.GetAsync(input.SourceRunId.Value);
            if (sourceRun.ProfileId != input.ProfileId || sourceRun.Status != RunStatus.Succeeded)
            {
                throw new UserFriendlyException("Selected source run does not belong to this profile or did not succeed.");
            }
        }
        else
        {
            var latestRunQuery = (await _runs.GetQueryableAsync())
                .Where(r => r.ProfileId == input.ProfileId && r.Status == RunStatus.Succeeded)
                .OrderByDescending(r => r.CreationTime);
            sourceRun = await FirstOrDefaultSafeAsync(latestRunQuery)
                ?? throw new UserFriendlyException("Cannot start analysis without a successful source run for this profile.");
        }

        var profile = await _profiles.GetAsync(input.ProfileId);
        var baseDefinition = ProfileJson.Parse(profile.ApprovedDefinitionJson ?? profile.DefinitionJson);
        var baseHash = AnalysisReplayService.ComputeProfileHash(baseDefinition);

        var analysis = new ReconciliationAnalysis(Guid.NewGuid())
        {
            ProfileId = input.ProfileId,
            SourceRunId = sourceRun.Id,
            Status = AnalysisStatuses.Queued,
            Stage = "Queued",
            Context = (input.Context ?? "").Trim(),
            BaseHash = baseHash,
            CreatorId = actorId,
        };

        await _analyses.InsertAsync(analysis, autoSave: true);

        slot.ActiveAnalysisId = analysis.Id;
        slot.LastAnalysisId = analysis.Id;
        slot.ConcurrencyToken = Guid.NewGuid().ToString("N");
        await _slots.UpdateAsync(slot, autoSave: true);

        var receipt = new ConfigurationRequestReceipt(Guid.NewGuid())
        {
            ActorId = actorId,
            OperationKind = "StartOrResume",
            RequestId = input.RequestId,
            RequestBodyHash = requestBodyHash,
            TargetProfileId = input.ProfileId,
            TargetAnalysisId = analysis.Id,
            ResultId = analysis.Id,
            CreationTime = DateTime.UtcNow,
        };
        await _receipts.InsertAsync(receipt, autoSave: true);

        // Enqueue background analysis job
        await _background.EnqueueAsync(new ReconciliationAnalysisJobArgs { AnalysisId = analysis.Id });

        return new StartOrResumeAnalysisResultDto
        {
            AnalysisId = analysis.Id,
            Reused = false,
            Status = AnalysisStatuses.Queued,
            Revision = baseHash,
            Message = "Analysis queued successfully.",
        };
    }

    public async Task<ReconciliationAnalysisDetailDto> GetAsync(Guid id)
    {
        var analysis = await _analyses.GetAsync(id);
        var profile = await _profiles.GetAsync(analysis.ProfileId);
        var sourceRun = await _runs.GetAsync(analysis.SourceRunId);

        ReconciliationAnalysisValidation? validation = null;
        if (analysis.PreviewId.HasValue)
        {
            validation = await _validations.FindAsync(analysis.PreviewId.Value);
        }

        AnalysisImpactSummaryDto? impactDto = null;
        if (validation != null)
        {
            impactDto = MapValidationToImpactDto(validation);
        }

        // Extract diagnoses from manifest
        var diagnoses = new List<DiagnosisItemDto>();
        if (!string.IsNullOrWhiteSpace(analysis.ManifestJson))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<AnalysisEvidenceManifest>(analysis.ManifestJson, ProfileJson.Options);
                if (manifest?.InitialDiagnoses != null) diagnoses.AddRange(manifest.InitialDiagnoses);
            }
            catch { }
        }

        // Execution status
        Guid? executionId = null;
        string? executionStatus = null;
        Guid? resultingRunId = null;
        if (analysis.ConfigurationCommitId.HasValue)
        {
            var exec = await _executions.FirstOrDefaultAsync(e => e.CommitId == analysis.ConfigurationCommitId.Value);
            if (exec != null)
            {
                executionId = exec.Id;
                executionStatus = exec.Status;
                resultingRunId = exec.ResultingRunId;
            }
        }

        var selectedGroupIds = string.IsNullOrWhiteSpace(analysis.SelectionJson)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(analysis.SelectionJson) ?? [];

        var canAccept = analysis.Status == AnalysisStatuses.AwaitingDecision &&
                        validation != null &&
                        validation.PreservedMatches &&
                        selectedGroupIds.Count > 0;

        var canReject = analysis.Status is AnalysisStatuses.AwaitingDecision or AnalysisStatuses.Stale or AnalysisStatuses.Blocked;
        var canRevalidate = analysis.Status is AnalysisStatuses.Stale or AnalysisStatuses.Blocked;
        var canRetry = analysis.Status == AnalysisStatuses.Failed;
        var canRun = analysis.Status == AnalysisStatuses.Accepted && analysis.ConfigurationCommitId.HasValue;

        string? blockerReason = null;
        if (analysis.Status == AnalysisStatuses.AwaitingDecision && validation != null && !validation.PreservedMatches)
        {
            blockerReason = "Candidate selection fails match preservation or regression checks.";
        }

        return new ReconciliationAnalysisDetailDto
        {
            Id = analysis.Id,
            ProfileId = analysis.ProfileId,
            ProfileName = profile.Name,
            ProfileVersion = profile.Version,
            SourceRunId = analysis.SourceRunId,
            SourceRunTime = sourceRun.CreationTime,
            SourceRunTotalRecords = sourceRun.TotalRecords,
            SourceRunMatchedRecords = sourceRun.MatchedRecords,
            SourceRunUnresolvedRecords = sourceRun.UnresolvedRecords,
            Datasets = ProfileJson.Parse(profile.ApprovedDefinitionJson ?? profile.DefinitionJson).Datasets,
            Status = analysis.Status,
            Stage = analysis.Stage,
            CreationTime = analysis.CreationTime,
            CompletionTime = analysis.CompletionTime,
            Revision = analysis.BaseHash,
            Context = analysis.Context,
            SelectedGroupIds = selectedGroupIds,
            SelectionHash = analysis.SelectionHash,
            Impact = impactDto,
            Diagnoses = diagnoses,
            ConfigurationCommitId = analysis.ConfigurationCommitId,
            CommittedProfileVersion = analysis.ConfigurationCommitId.HasValue ? profile.Version : null,
            ExecutionId = executionId,
            ExecutionStatus = executionStatus,
            ResultingRunId = resultingRunId,
            FailureCode = analysis.FailureCode,
            FailureMessage = analysis.FailureMessage,
            CanAccept = canAccept,
            CanReject = canReject,
            CanRevalidate = canRevalidate,
            CanRetry = canRetry,
            CanRun = canRun,
            BlockerReason = blockerReason,
        };
    }

    public async Task<List<AnalysisChangeDto>> GetChangesAsync(Guid id)
    {
        var changes = await _changes.GetListAsync(c => c.AnalysisId == id);
        return changes.Select(c => new AnalysisChangeDto
        {
            Id = c.Id,
            DependencyGroupId = c.DependencyGroupId,
            ChangeType = c.ChangeType,
            TargetIdentifier = c.TargetIdentifier,
            TargetObjectVersion = c.TargetObjectVersion,
            OperationType = c.OperationType,
            OperationJson = c.OperationJson,
            BeforeJson = c.BeforeJson,
            AfterJson = c.AfterJson,
            Rationale = c.Rationale,
            EvidenceIds = string.IsNullOrWhiteSpace(c.EvidenceIdsJson) ? [] : JsonSerializer.Deserialize<List<string>>(c.EvidenceIdsJson) ?? [],
            SafetyClass = c.SafetyClass,
            IsSelectable = c.IsSelectable,
            BlockerReason = c.BlockerReason,
        }).ToList();
    }

    public async Task<List<ProfileAnalysisSummaryDto>> GetProfileStatusesAsync(BatchProfileStatusesInputDto input)
    {
        var summaries = new List<ProfileAnalysisSummaryDto>();
        var profileIds = input.ProfileIds.Distinct().Take(50).ToList();

        foreach (var profileId in profileIds)
        {
            var profile = await _profiles.FindAsync(profileId);
            if (profile == null) continue;

            var slot = await _slots.FindAsync(profileId);
            ReconciliationAnalysis? activeAnalysis = null;
            if (slot?.ActiveAnalysisId != null)
            {
                activeAnalysis = await _analyses.FindAsync(slot.ActiveAnalysisId.Value);
            }

            ReconciliationAnalysis? lastAnalysis = null;
            if (slot?.LastAnalysisId != null && (activeAnalysis == null || slot.LastAnalysisId != activeAnalysis.Id))
            {
                lastAnalysis = await _analyses.FindAsync(slot.LastAnalysisId.Value);
            }

            var latestRunQuery = (await _runs.GetQueryableAsync())
                .Where(r => r.ProfileId == profileId && r.Status == RunStatus.Succeeded)
                .OrderByDescending(r => r.CreationTime);
            var latestRun = await FirstOrDefaultSafeAsync(latestRunQuery);

            string actionLabel;
            string actionType;
            string? explanation = null;

            if (activeAnalysis != null)
            {
                switch (activeAnalysis.Status)
                {
                    case AnalysisStatuses.Queued:
                    case AnalysisStatuses.Analyzing:
                    case AnalysisStatuses.Validating:
                        actionLabel = "View analysis";
                        actionType = "reopen";
                        break;
                    case AnalysisStatuses.AwaitingDecision:
                        actionLabel = "Review changes";
                        actionType = "review";
                        break;
                    case AnalysisStatuses.Stale:
                    case AnalysisStatuses.Blocked:
                        actionLabel = "Review assessment";
                        actionType = "review";
                        break;
                    default:
                        actionLabel = "Analyze";
                        actionType = "start";
                        break;
                }
            }
            else if (lastAnalysis?.Status == AnalysisStatuses.Accepted)
            {
                actionLabel = "View accepted changes";
                actionType = "view_accepted";
            }
            else if (latestRun == null)
            {
                actionLabel = "Create run";
                actionType = "no_run";
                explanation = "No successful run yet. Run reconciliation once to enable AI optimization.";
            }
            else
            {
                actionLabel = "Analyze";
                actionType = "start";
            }

            summaries.Add(new ProfileAnalysisSummaryDto
            {
                ProfileId = profileId,
                ActiveAnalysisId = activeAnalysis?.Id,
                ActiveStatus = activeAnalysis?.Status,
                ActiveStage = activeAnalysis?.Stage,
                LastAnalysisId = lastAnalysis?.Id,
                LastStatus = lastAnalysis?.Status,
                ActiveCommitId = profile.ActiveConfigurationCommitId,
                ActiveCommitVersion = profile.Version,
                LatestRunId = latestRun?.Id,
                HasSuccessfulRun = latestRun != null,
                ActionLabel = actionLabel,
                ActionType = actionType,
                Explanation = explanation,
            });
        }

        return summaries;
    }

    public async Task<List<ReconciliationAnalysisDetailDto>> GetHistoryAsync(Guid profileId)
    {
        var query = (await _analyses.GetQueryableAsync())
            .Where(a => a.ProfileId == profileId)
            .OrderByDescending(a => a.CreationTime)
            .Take(10);

        var list = await ToListSafeAsync(query);
        var result = new List<ReconciliationAnalysisDetailDto>();
        foreach (var item in list)
        {
            result.Add(await GetAsync(item.Id));
        }
        return result;
    }

    public async Task<ReconciliationAnalysisDetailDto> UpdateSelectionAsync(Guid id, UpdateAnalysisSelectionDto input)
    {
        var analysis = await _analyses.GetAsync(id);
        if (analysis.Status is AnalysisStatuses.Accepted or AnalysisStatuses.Rejected)
        {
            throw new UserFriendlyException("Cannot update selection on a completed assessment.");
        }

        var profile = await _profiles.GetAsync(analysis.ProfileId);
        var sourceRun = await _runs.GetAsync(analysis.SourceRunId);
        var allChanges = await _changes.GetListAsync(c => c.AnalysisId == id);

        var datasets = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(sourceRun.DatasetSnapshotJson)
            ?? throw new InvalidOperationException("Source run does not contain dataset snapshot.");

        var baseDefinition = ProfileJson.Parse(profile.ApprovedDefinitionJson ?? profile.DefinitionJson);

        analysis.SelectionJson = JsonSerializer.Serialize(input.SelectedGroupIds);
        analysis.SelectionHash = AnalysisReplayService.ComputeSelectionHash(input.SelectedGroupIds);

        var eval = await _replay.EvaluateCombinationAsync(
            profile,
            baseDefinition,
            allChanges,
            input.SelectedGroupIds,
            datasets,
            sourceRun.Id);

        eval.Validation.AnalysisId = analysis.Id;
        await _validations.InsertAsync(eval.Validation, autoSave: true);

        analysis.PreviewId = eval.Validation.Id;
        await _analyses.UpdateAsync(analysis, autoSave: true);

        return await GetAsync(id);
    }

    public async Task<ReconciliationAnalysisDetailDto> RevalidateAsync(Guid id)
    {
        var analysis = await _analyses.GetAsync(id);
        var selectedGroupIds = string.IsNullOrWhiteSpace(analysis.SelectionJson)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(analysis.SelectionJson) ?? [];

        return await UpdateSelectionAsync(id, new UpdateAnalysisSelectionDto
        {
            ExpectedRevision = analysis.BaseHash,
            SelectedGroupIds = selectedGroupIds,
        });
    }

    public async Task<DecisionResultDto> MakeDecisionAsync(Guid id, ReconciliationAnalysisDecisionDto input)
    {
        var actorId = GetCurrentUserId();
        if (input.RequestId == Guid.Empty) throw new UserFriendlyException("RequestId is required for decision.");

        var receiptHash = ComputeHash(JsonSerializer.Serialize(new { input.Decision, input.SelectionHash }));
        var existingReceipt = await _receipts.FirstOrDefaultAsync(r =>
            r.ActorId == actorId && r.OperationKind == "Decision" && r.RequestId == input.RequestId);

        if (existingReceipt != null)
        {
            var priorAnalysis = await _analyses.GetAsync(id);
            return new DecisionResultDto
            {
                AnalysisId = id,
                Decision = input.Decision,
                Status = priorAnalysis.Status,
                CommitId = priorAnalysis.ConfigurationCommitId,
            };
        }

        var analysis = await _analyses.GetAsync(id);
        if (analysis.Status != AnalysisStatuses.AwaitingDecision)
        {
            throw new UserFriendlyException($"Assessment cannot be decided from state '{analysis.Status}'.");
        }

        var profile = await _profiles.GetAsync(analysis.ProfileId);
        var allChanges = await _changes.GetListAsync(c => c.AnalysisId == id);

        if (string.Equals(input.Decision, "AcceptSelected", StringComparison.OrdinalIgnoreCase))
        {
            if (!analysis.PreviewId.HasValue)
            {
                throw new UserFriendlyException("No validation preview exists for this selection.");
            }

            var validation = await _validations.GetAsync(analysis.PreviewId.Value);
            var selectedGroupIds = string.IsNullOrWhiteSpace(analysis.SelectionJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(analysis.SelectionJson) ?? [];

            var commit = await _commitService.CommitSelectedChangesAsync(
                profile,
                analysis,
                validation,
                allChanges,
                selectedGroupIds,
                input.RequestId,
                actorId);

            await _analyses.UpdateAsync(analysis, autoSave: true);

            var receipt = new ConfigurationRequestReceipt(Guid.NewGuid())
            {
                ActorId = actorId,
                OperationKind = "Decision",
                RequestId = input.RequestId,
                RequestBodyHash = receiptHash,
                TargetProfileId = profile.Id,
                TargetAnalysisId = analysis.Id,
                ResultId = commit.Id,
                CreationTime = DateTime.UtcNow,
            };
            await _receipts.InsertAsync(receipt, autoSave: true);

            return new DecisionResultDto
            {
                AnalysisId = id,
                Decision = "AcceptSelected",
                Status = AnalysisStatuses.Accepted,
                CommitId = commit.Id,
                CommittedProfileVersion = commit.ProfileVersion,
            };
        }
        else if (string.Equals(input.Decision, "RejectAll", StringComparison.OrdinalIgnoreCase))
        {
            await _commitService.RejectAllAsync(profile, analysis, input.RequestId, actorId);
            await _analyses.UpdateAsync(analysis, autoSave: true);

            var receipt = new ConfigurationRequestReceipt(Guid.NewGuid())
            {
                ActorId = actorId,
                OperationKind = "Decision",
                RequestId = input.RequestId,
                RequestBodyHash = receiptHash,
                TargetProfileId = profile.Id,
                TargetAnalysisId = analysis.Id,
                ResultId = analysis.Id,
                CreationTime = DateTime.UtcNow,
            };
            await _receipts.InsertAsync(receipt, autoSave: true);

            return new DecisionResultDto
            {
                AnalysisId = id,
                Decision = "RejectAll",
                Status = AnalysisStatuses.Rejected,
            };
        }
        else
        {
            throw new UserFriendlyException($"Unsupported decision '{input.Decision}'.");
        }
    }

    public async Task<ReconciliationAnalysisDetailDto> RetryAsync(Guid id)
    {
        var analysis = await _analyses.GetAsync(id);
        if (analysis.Status != AnalysisStatuses.Failed)
        {
            throw new UserFriendlyException("Only failed analyses can be retried.");
        }

        analysis.Status = AnalysisStatuses.Queued;
        analysis.Stage = "Retrying";
        analysis.FailureCode = null;
        analysis.FailureMessage = null;
        await _analyses.UpdateAsync(analysis, autoSave: true);

        await _background.EnqueueAsync(new ReconciliationAnalysisJobArgs { AnalysisId = analysis.Id });

        return await GetAsync(id);
    }

    public async Task<ConfigurationExecutionDto> ExecuteAsync(Guid id, ExecuteAnalysisInputDto input)
    {
        var actorId = GetCurrentUserId();
        if (input.RequestId == Guid.Empty) throw new UserFriendlyException("RequestId is required for run execution.");

        var analysis = await _analyses.GetAsync(id);
        if (analysis.Status != AnalysisStatuses.Accepted || !analysis.ConfigurationCommitId.HasValue)
        {
            throw new UserFriendlyException("Cannot execute an assessment that has not been accepted.");
        }

        var commit = await _commits.GetAsync(analysis.ConfigurationCommitId.Value);

        // Check for existing execution on this commit
        var existingExecution = await _executions.FirstOrDefaultAsync(e =>
            e.CommitId == commit.Id && e.AssessedInputManifestHash == analysis.ManifestHash);

        if (existingExecution != null)
        {
            return new ConfigurationExecutionDto
            {
                ExecutionId = existingExecution.Id,
                CommitId = existingExecution.CommitId,
                Status = existingExecution.Status,
                Stage = existingExecution.Stage,
                ResultingRunId = existingExecution.ResultingRunId,
                ErrorMessage = existingExecution.ErrorMessage,
                Attempt = existingExecution.Attempt,
            };
        }

        var execution = new ConfigurationExecution(Guid.NewGuid())
        {
            CommitId = commit.Id,
            ExecutionRequestId = input.RequestId,
            AssessedInputManifestHash = analysis.ManifestHash,
            Status = "Queued",
            Stage = "Queued",
            CreatorId = actorId,
        };

        await _executions.InsertAsync(execution, autoSave: true);

        // Enqueue background execution
        await _background.EnqueueAsync(new ConfigurationExecutionJobArgs { ExecutionId = execution.Id });

        return new ConfigurationExecutionDto
        {
            ExecutionId = execution.Id,
            CommitId = execution.CommitId,
            Status = "Queued",
            Stage = "Queued",
            Attempt = 1,
        };
    }

    public async Task<ConfigurationExecutionDto> GetExecutionAsync(Guid id)
    {
        var analysis = await _analyses.GetAsync(id);
        if (!analysis.ConfigurationCommitId.HasValue)
        {
            throw new UserFriendlyException("This assessment has no committed configuration.");
        }

        var execution = await _executions.FirstOrDefaultAsync(e => e.CommitId == analysis.ConfigurationCommitId.Value);
        if (execution == null)
        {
            return new ConfigurationExecutionDto
            {
                CommitId = analysis.ConfigurationCommitId.Value,
                Status = "NotRequested",
                Stage = "NotRequested",
            };
        }

        return new ConfigurationExecutionDto
        {
            ExecutionId = execution.Id,
            CommitId = execution.CommitId,
            Status = execution.Status,
            Stage = execution.Stage,
            ResultingRunId = execution.ResultingRunId,
            ErrorMessage = execution.ErrorMessage,
            Attempt = execution.Attempt,
        };
    }

    private static AnalysisImpactSummaryDto MapValidationToImpactDto(ReconciliationAnalysisValidation v)
    {
        var impact = string.IsNullOrWhiteSpace(v.DetailedReportJson)
            ? new AnalysisImpactReport()
            : JsonSerializer.Deserialize<AnalysisImpactReport>(v.DetailedReportJson, ProfileJson.Options) ?? new();

        var regressionVerdicts = string.IsNullOrWhiteSpace(v.RegressionVerdictsJson)
            ? new List<RegressionVerdictDto>()
            : JsonSerializer.Deserialize<List<RegressionVerdictDto>>(v.RegressionVerdictsJson, ProfileJson.Options) ?? [];

        return new AnalysisImpactSummaryDto
        {
            TotalRecords = v.TotalRecords,
            BaseFullMatchRecords = v.BaseFullMatchCount,
            BasePartialMatchRecords = v.BasePartialMatchCount,
            BaseUnresolvedRecords = v.BaseUnresolvedCount,
            BaseAmbiguousRecords = v.BaseAmbiguousCount,
            CandidateFullMatchRecords = v.FullMatchCount,
            CandidatePartialMatchRecords = v.PartialMatchCount,
            CandidateUnresolvedRecords = v.UnresolvedCount,
            CandidateAmbiguousRecords = v.AmbiguousCount,
            RecoveredUnresolvedRecords = impact.RecoveredUnresolvedRecords,
            ProtectedMatchedRecords = impact.ProtectedMatchedRecords,

            BaseFullMatchPercentage = impact.BaseFullMatchPercentage,
            BasePartialMatchPercentage = impact.BasePartialMatchPercentage,
            BaseAnyMatchPercentage = impact.BaseAnyMatchPercentage,

            CandidateFullMatchPercentage = impact.CandidateFullMatchPercentage,
            CandidatePartialMatchPercentage = impact.CandidatePartialMatchPercentage,
            CandidateAnyMatchPercentage = impact.CandidateAnyMatchPercentage,

            FullImprovementPercentagePoints = impact.FullImprovementPercentagePoints,
            AnyImprovementPercentagePoints = impact.AnyImprovementPercentagePoints,
            RecoveredUnresolvedPercentage = impact.RecoveredUnresolvedPercentage,

            PreservedMatches = v.PreservedMatches,
            PreservedGroupsCount = impact.Preservation.PreservedGroupsCount,
            LostGroupsCount = impact.Preservation.LostGroupsCount,
            DegradedGroupsCount = impact.Preservation.DegradedGroupsCount,
            ChangedMembersGroupsCount = impact.Preservation.ChangedMembersGroupsCount,
            PreservationViolations = impact.Preservation.Violations,

            Coverage = v.Coverage,
            RegressionVerdicts = regressionVerdicts,
            DatasetBreakdowns = impact.DatasetBreakdowns.Select(d => new DatasetBreakdownDto
            {
                DatasetCode = d.DatasetCode,
                TotalRecords = d.TotalRecords,
                BaseFullMatchRecords = d.BaseFullMatchRecords,
                BasePartialMatchRecords = d.BasePartialMatchRecords,
                BaseUnresolvedRecords = d.BaseUnresolvedRecords,
                BaseAmbiguousRecords = d.BaseAmbiguousRecords,
                CandidateFullMatchRecords = d.CandidateFullMatchRecords,
                CandidatePartialMatchRecords = d.CandidatePartialMatchRecords,
                CandidateUnresolvedRecords = d.CandidateUnresolvedRecords,
                CandidateAmbiguousRecords = d.CandidateAmbiguousRecords,
            }).ToList(),
            GroupContributions = impact.GroupContributions.Select(g => new GroupContributionDto
            {
                GroupId = g.GroupId,
                NewlyMatchedRecords = g.NewlyMatchedRecords,
                NewlyMatchedGroups = g.NewlyMatchedGroups,
                FullMatches = g.FullMatches,
                PartialMatches = g.PartialMatches,
                IndependentAnyGainPoints = g.IndependentAnyGainPoints,
                MarginalAnyGainPoints = g.MarginalAnyGainPoints,
            }).ToList(),
        };
    }

    private static string ComputeHash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
