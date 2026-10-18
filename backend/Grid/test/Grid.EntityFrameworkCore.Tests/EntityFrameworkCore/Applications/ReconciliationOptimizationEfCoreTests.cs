using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grid.Reconciliation;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Grid.EntityFrameworkCore.Applications;

[Collection(GridTestConsts.CollectionDefinitionName)]
public class ReconciliationOptimizationEfCoreTests : GridTestBase<GridEntityFrameworkCoreTestModule>
{
    private readonly IRepository<ProfileAnalysisSlot, Guid> _slots;
    private readonly IRepository<ReconciliationAnalysis, Guid> _analyses;
    private readonly IRepository<ReconciliationAnalysisChange, Guid> _changes;
    private readonly IRepository<ReconciliationAnalysisValidation, Guid> _validations;
    private readonly IRepository<ConfigurationCommit, Guid> _commits;
    private readonly IRepository<ConfigurationExecution, Guid> _executions;
    private readonly IRepository<ConfigurationRequestReceipt, Guid> _receipts;

    public ReconciliationOptimizationEfCoreTests()
    {
        _slots = GetRequiredService<IRepository<ProfileAnalysisSlot, Guid>>();
        _analyses = GetRequiredService<IRepository<ReconciliationAnalysis, Guid>>();
        _changes = GetRequiredService<IRepository<ReconciliationAnalysisChange, Guid>>();
        _validations = GetRequiredService<IRepository<ReconciliationAnalysisValidation, Guid>>();
        _commits = GetRequiredService<IRepository<ConfigurationCommit, Guid>>();
        _executions = GetRequiredService<IRepository<ConfigurationExecution, Guid>>();
        _receipts = GetRequiredService<IRepository<ConfigurationRequestReceipt, Guid>>();
    }

    [Fact]
    public async Task Can_Persist_And_Retrieve_Analysis_Lifecycle_Entities()
    {
        var profileId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();

        // 1. Slot
        var slot = new ProfileAnalysisSlot(profileId)
        {
            ActiveAnalysisId = analysisId,
            LastAnalysisId = analysisId
        };
        await _slots.InsertAsync(slot, autoSave: true);

        var retrievedSlot = await _slots.FindAsync(profileId);
        Assert.NotNull(retrievedSlot);
        Assert.Equal(analysisId, retrievedSlot.ActiveAnalysisId);

        // 2. Analysis
        var analysis = new ReconciliationAnalysis(analysisId)
        {
            ProfileId = profileId,
            SourceRunId = Guid.NewGuid(),
            Status = AnalysisStatuses.AwaitingDecision,
            BaseHash = "base-hash-abc",
            ManifestHash = "manifest-hash-123",
            Stage = "Ready",
            PromptModelProvenanceJson = "{\"source\":\"test-advisor\"}"
        };
        await _analyses.InsertAsync(analysis, autoSave: true);

        var retrievedAnalysis = await _analyses.GetAsync(analysisId);
        Assert.NotNull(retrievedAnalysis);
        Assert.Equal(profileId, retrievedAnalysis.ProfileId);
        Assert.Equal(AnalysisStatuses.AwaitingDecision, retrievedAnalysis.Status);

        // 3. Change & Validation
        var changeId = Guid.NewGuid();
        var change = new ReconciliationAnalysisChange(changeId)
        {
            AnalysisId = analysisId,
            DependencyGroupId = "group-1",
            ChangeType = "profile",
            TargetIdentifier = "rule-1",
            OperationType = "append_rule",
            SafetyClass = "safe_additive",
            OperationJson = "{\"type\":\"append_rule\"}",
            IsSelectable = true
        };
        await _changes.InsertAsync(change, autoSave: true);

        var validationId = Guid.NewGuid();
        var validation = new ReconciliationAnalysisValidation(validationId)
        {
            AnalysisId = analysisId,
            State = "Completed",
            Coverage = "FullReplay",
            PreservedMatches = true,
            TotalRecords = 100,
            FullMatchCount = 80,
            UnresolvedCount = 20
        };
        await _validations.InsertAsync(validation, autoSave: true);

        // 4. Commit & Execution
        var commitId = Guid.NewGuid();
        var commit = new ConfigurationCommit(commitId)
        {
            ProfileId = profileId,
            ProfileVersion = 2,
            ContentHash = "manifest-hash-123",
            DecisionRequestId = Guid.NewGuid()
        };
        await _commits.InsertAsync(commit, autoSave: true);

        var execId = Guid.NewGuid();
        var exec = new ConfigurationExecution(execId)
        {
            CommitId = commitId,
            ExecutionRequestId = Guid.NewGuid(),
            Status = "Queued"
        };
        await _executions.InsertAsync(exec, autoSave: true);

        // 5. Request Receipt
        var receiptId = Guid.NewGuid();
        var receipt = new ConfigurationRequestReceipt(receiptId)
        {
            ActorId = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            OperationKind = "StartOrResume",
            RequestBodyHash = "payload-hash-xyz",
            ResultId = analysisId
        };
        await _receipts.InsertAsync(receipt, autoSave: true);

        // Verify queries
        var persistedChanges = await _changes.GetListAsync(c => c.AnalysisId == analysisId);
        Assert.Single(persistedChanges);
        Assert.Equal("group-1", persistedChanges[0].DependencyGroupId);

        var persistedValidation = await _validations.GetAsync(validationId);
        Assert.True(persistedValidation.PreservedMatches);
        Assert.Equal("Completed", persistedValidation.State);

        var persistedCommit = await _commits.GetAsync(commitId);
        Assert.Equal(2, persistedCommit.ProfileVersion);

        var persistedExec = await _executions.GetAsync(execId);
        Assert.Equal("Queued", persistedExec.Status);

        var persistedReceipt = await _receipts.GetAsync(receiptId);
        Assert.Equal("StartOrResume", persistedReceipt.OperationKind);
    }
}
