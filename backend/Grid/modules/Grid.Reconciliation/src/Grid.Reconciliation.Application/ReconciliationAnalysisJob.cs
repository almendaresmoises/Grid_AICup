using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace Grid.Reconciliation;

public class ReconciliationAnalysisJobArgs
{
    public Guid AnalysisId { get; set; }
}

public class ReconciliationAnalysisJob : AsyncBackgroundJob<ReconciliationAnalysisJobArgs>, ITransientDependency
{
    private readonly IRepository<ReconciliationAnalysis, Guid> _analyses;
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IRepository<ReconciliationRun, Guid> _runs;
    private readonly IRepository<ReconciliationAnalysisChange, Guid> _changes;
    private readonly IRepository<ReconciliationAnalysisValidation, Guid> _validations;
    private readonly IRepository<ProfileAnalysisSlot, Guid> _slots;
    private readonly IReconciliationOptimizationAdvisor _advisor;
    private readonly AnalysisReplayService _replay;
    private readonly IUnitOfWorkManager _uow;
    private readonly ILogger<ReconciliationAnalysisJob> _logger;

    public ReconciliationAnalysisJob(
        IRepository<ReconciliationAnalysis, Guid> analyses,
        IRepository<Profile, Guid> profiles,
        IRepository<ReconciliationRun, Guid> runs,
        IRepository<ReconciliationAnalysisChange, Guid> changes,
        IRepository<ReconciliationAnalysisValidation, Guid> validations,
        IRepository<ProfileAnalysisSlot, Guid> slots,
        IReconciliationOptimizationAdvisor advisor,
        AnalysisReplayService replay,
        IUnitOfWorkManager uow,
        ILogger<ReconciliationAnalysisJob> logger)
    {
        _analyses = analyses;
        _profiles = profiles;
        _runs = runs;
        _changes = changes;
        _validations = validations;
        _slots = slots;
        _advisor = advisor;
        _replay = replay;
        _uow = uow;
        _logger = logger;
    }

    public override async Task ExecuteAsync(ReconciliationAnalysisJobArgs args)
    {
        using var unit = _uow.Begin(requiresNew: true, isTransactional: true);
        var analysis = await _analyses.FindAsync(args.AnalysisId);
        if (analysis == null || analysis.Status is AnalysisStatuses.Accepted or AnalysisStatuses.Rejected or AnalysisStatuses.NoRecommendations)
        {
            return;
        }

        try
        {
            analysis.Status = AnalysisStatuses.Analyzing;
            analysis.Stage = "Diagnosing";
            await _analyses.UpdateAsync(analysis, autoSave: true);

            var profile = await _profiles.GetAsync(analysis.ProfileId);
            var sourceRun = await _runs.GetAsync(analysis.SourceRunId);

            var datasets = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(sourceRun.DatasetSnapshotJson)
                ?? throw new InvalidOperationException("Source run does not contain dataset snapshot.");

            var baseDefinition = ProfileJson.Parse(profile.ApprovedDefinitionJson ?? profile.DefinitionJson);
            var baseResult = ReconciliationEngine.Reconcile(datasets, baseDefinition);

            // Build Evidence
            var manifest = AnalysisEvidenceBuilder.BuildManifest(profile, baseDefinition, sourceRun, baseResult, datasets);
            analysis.ManifestJson = JsonSerializer.Serialize(manifest, ProfileJson.Options);
            analysis.ManifestHash = AnalysisReplayService.ComputeDatasetsHash(datasets);
            analysis.BaseHash = AnalysisReplayService.ComputeProfileHash(baseDefinition);

            if (manifest.HasNoUnresolved)
            {
                analysis.Status = AnalysisStatuses.NoRecommendations;
                analysis.Stage = "NoRecommendations";
                analysis.CompletionTime = DateTime.UtcNow;
                await _analyses.UpdateAsync(analysis, autoSave: true);
                await ReleaseSlotAsync(profile.Id, analysis.Id);
                await unit.CompleteAsync();
                return;
            }

            // Advise
            analysis.Stage = "RequestingAdvisor";
            await _analyses.UpdateAsync(analysis, autoSave: true);

            var advisorResult = await _advisor.AdviseAsync(new OptimizationAdvisorInput
            {
                BaseProfile = baseDefinition,
                Rows = datasets,
                Manifest = manifest,
                Context = analysis.Context,
            });

            analysis.PromptModelProvenanceJson = advisorResult.PromptModelProvenanceJson;

            if (advisorResult.ChangeGroups.Count == 0)
            {
                analysis.Status = AnalysisStatuses.NoRecommendations;
                analysis.Stage = "NoRecommendations";
                analysis.CompletionTime = DateTime.UtcNow;
                await _analyses.UpdateAsync(analysis, autoSave: true);
                await ReleaseSlotAsync(profile.Id, analysis.Id);
                await unit.CompleteAsync();
                return;
            }

            // Persist changes
            var persistedChanges = new List<ReconciliationAnalysisChange>();
            foreach (var group in advisorResult.ChangeGroups)
            {
                foreach (var op in group.Operations)
                {
                    var change = new ReconciliationAnalysisChange(Guid.NewGuid())
                    {
                        AnalysisId = analysis.Id,
                        DependencyGroupId = group.GroupId,
                        ChangeType = op.ChangeType,
                        TargetIdentifier = op.TargetIdentifier,
                        OperationType = op.OperationType,
                        OperationJson = op.OperationJson,
                        BeforeJson = op.BeforeJson,
                        AfterJson = op.AfterJson,
                        Rationale = op.Rationale,
                        EvidenceIdsJson = JsonSerializer.Serialize(op.EvidenceIds),
                        SafetyClass = op.SafetyClass,
                        IsSelectable = op.IsSelectable,
                        BlockerReason = op.BlockerReason,
                        BaseHash = analysis.BaseHash,
                    };
                    await _changes.InsertAsync(change, autoSave: true);
                    persistedChanges.Add(change);
                }
            }

            // Initial selection: all selectable groups
            var initialSelectedGroupIds = advisorResult.ChangeGroups
                .Where(g => g.Operations.All(o => o.IsSelectable))
                .Select(g => g.GroupId)
                .ToList();

            analysis.SelectionJson = JsonSerializer.Serialize(initialSelectedGroupIds);
            analysis.SelectionHash = AnalysisReplayService.ComputeSelectionHash(initialSelectedGroupIds);
            analysis.Status = AnalysisStatuses.Validating;
            analysis.Stage = "ValidatingProposals";
            await _analyses.UpdateAsync(analysis, autoSave: true);

            // Validate combination
            var evalResult = await _replay.EvaluateCombinationAsync(
                profile,
                baseDefinition,
                persistedChanges,
                initialSelectedGroupIds,
                datasets,
                sourceRun.Id);

            evalResult.Validation.AnalysisId = analysis.Id;
            await _validations.InsertAsync(evalResult.Validation, autoSave: true);

            analysis.PreviewId = evalResult.Validation.Id;
            analysis.Status = AnalysisStatuses.AwaitingDecision;
            analysis.Stage = "AwaitingDecision";
            await _analyses.UpdateAsync(analysis, autoSave: true);

            await unit.CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze profile for analysis {Id}", analysis.Id);
            analysis.Status = AnalysisStatuses.Failed;
            analysis.Stage = "Failed";
            analysis.FailureCode = ex.GetType().Name;
            analysis.FailureMessage = ex.Message;
            analysis.CompletionTime = DateTime.UtcNow;
            await _analyses.UpdateAsync(analysis, autoSave: true);
            await ReleaseSlotAsync(analysis.ProfileId, analysis.Id);
            await unit.CompleteAsync();
        }
    }

    private async Task ReleaseSlotAsync(Guid profileId, Guid analysisId)
    {
        var slot = await _slots.FindAsync(profileId);
        if (slot != null && slot.ActiveAnalysisId == analysisId)
        {
            slot.ActiveAnalysisId = null;
            slot.LastAnalysisId = analysisId;
            slot.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await _slots.UpdateAsync(slot, autoSave: true);
        }
    }
}
