using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Grid.Reconciliation;

public class ConfigurationCommitService : ITransientDependency
{
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IRepository<ConfigurationCommit, Guid> _commits;
    private readonly IRepository<ProfileAnalysisSlot, Guid> _slots;

    public ConfigurationCommitService(
        IRepository<Profile, Guid> profiles,
        IRepository<ConfigurationCommit, Guid> commits,
        IRepository<ProfileAnalysisSlot, Guid> slots)
    {
        _profiles = profiles;
        _commits = commits;
        _slots = slots;
    }

    public async Task<ConfigurationCommit> CommitSelectedChangesAsync(
        Profile profile,
        ReconciliationAnalysis analysis,
        ReconciliationAnalysisValidation validation,
        List<ReconciliationAnalysisChange> allChanges,
        List<string> selectedGroupIds,
        Guid decisionRequestId,
        Guid actorId)
    {
        if (selectedGroupIds.Count == 0)
        {
            throw new UserFriendlyException("Cannot accept an empty selection. Choose at least one improvement group or select 'Reject all'.");
        }

        if (!validation.PreservedMatches)
        {
            throw new UserFriendlyException("Cannot accept changes that do not pass match preservation and regression checks.");
        }

        var baseDefinition = ProfileJson.Parse(profile.ApprovedDefinitionJson ?? profile.DefinitionJson);
        var selectedChanges = allChanges.Where(c => selectedGroupIds.Contains(c.DependencyGroupId)).ToList();
        var rejectedChanges = allChanges.Where(c => !selectedGroupIds.Contains(c.DependencyGroupId)).ToList();

        var candidateDefinition = AnalysisReplayService.CompileCandidateProfile(baseDefinition, selectedChanges);
        candidateDefinition.Version = profile.Version + 1;
        var approvedJson = ProfileJson.Write(candidateDefinition);

        profile.Version += 1;
        profile.ApprovedDefinitionJson = approvedJson;
        profile.DefinitionJson = approvedJson;
        profile.Status = ProfileStatus.Approved;

        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(approvedJson)));

        var commit = new ConfigurationCommit(Guid.NewGuid())
        {
            ProfileId = profile.Id,
            ProfileVersion = profile.Version,
            ApprovedProfileSnapshotJson = approvedJson,
            SelectedOperationIdsJson = JsonSerializer.Serialize(selectedChanges.Select(c => c.Id)),
            RejectedOperationIdsJson = JsonSerializer.Serialize(rejectedChanges.Select(c => c.Id)),
            ParentCommitId = profile.ActiveConfigurationCommitId,
            BaseHash = analysis.BaseHash,
            ContentHash = contentHash,
            ValidationReportHash = validation.SelectionHash,
            ActorId = actorId,
            DecisionRequestId = decisionRequestId,
            CreationTime = DateTime.UtcNow,
        };

        await _commits.InsertAsync(commit, autoSave: true);

        profile.ActiveConfigurationCommitId = commit.Id;
        await _profiles.UpdateAsync(profile, autoSave: true);

        analysis.Status = AnalysisStatuses.Accepted;
        analysis.Stage = "Accepted";
        analysis.ConfigurationCommitId = commit.Id;
        analysis.DecisionActorId = actorId;
        analysis.DecisionTime = DateTime.UtcNow;
        analysis.DecisionRequestId = decisionRequestId;
        analysis.CompletionTime = DateTime.UtcNow;

        // Release profile analysis slot
        var slot = await _slots.FindAsync(profile.Id);
        if (slot != null)
        {
            slot.ActiveAnalysisId = null;
            slot.LastAnalysisId = analysis.Id;
            slot.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await _slots.UpdateAsync(slot, autoSave: true);
        }

        return commit;
    }

    public async Task RejectAllAsync(
        Profile profile,
        ReconciliationAnalysis analysis,
        Guid decisionRequestId,
        Guid actorId)
    {
        analysis.Status = AnalysisStatuses.Rejected;
        analysis.Stage = "Rejected";
        analysis.DecisionActorId = actorId;
        analysis.DecisionTime = DateTime.UtcNow;
        analysis.DecisionRequestId = decisionRequestId;
        analysis.CompletionTime = DateTime.UtcNow;

        // Release profile analysis slot
        var slot = await _slots.FindAsync(profile.Id);
        if (slot != null)
        {
            slot.ActiveAnalysisId = null;
            slot.LastAnalysisId = analysis.Id;
            slot.ConcurrencyToken = Guid.NewGuid().ToString("N");
            await _slots.UpdateAsync(slot, autoSave: true);
        }
    }
}
