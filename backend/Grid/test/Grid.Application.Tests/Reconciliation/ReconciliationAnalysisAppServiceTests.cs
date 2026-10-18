using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;
using Xunit;

namespace Grid.Reconciliation;

public class ReconciliationAnalysisAppServiceTests
{
    private readonly IRepository<ReconciliationAnalysis, Guid> _analyses = Substitute.For<IRepository<ReconciliationAnalysis, Guid>>();
    private readonly IRepository<ProfileAnalysisSlot, Guid> _slots = Substitute.For<IRepository<ProfileAnalysisSlot, Guid>>();
    private readonly IRepository<ReconciliationAnalysisChange, Guid> _changes = Substitute.For<IRepository<ReconciliationAnalysisChange, Guid>>();
    private readonly IRepository<ReconciliationAnalysisValidation, Guid> _validations = Substitute.For<IRepository<ReconciliationAnalysisValidation, Guid>>();
    private readonly IRepository<ConfigurationCommit, Guid> _commits = Substitute.For<IRepository<ConfigurationCommit, Guid>>();
    private readonly IRepository<ConfigurationExecution, Guid> _executions = Substitute.For<IRepository<ConfigurationExecution, Guid>>();
    private readonly IRepository<ConfigurationRequestReceipt, Guid> _receipts = Substitute.For<IRepository<ConfigurationRequestReceipt, Guid>>();
    private readonly IRepository<Profile, Guid> _profiles = Substitute.For<IRepository<Profile, Guid>>();
    private readonly IRepository<ReconciliationRun, Guid> _runs = Substitute.For<IRepository<ReconciliationRun, Guid>>();
    private readonly IBackgroundJobManager _background = Substitute.For<IBackgroundJobManager>();
    private readonly AnalysisReplayService _replay;
    private readonly ConfigurationCommitService _commitService;

    public ReconciliationAnalysisAppServiceTests()
    {
        _commitService = new ConfigurationCommitService(
            _profiles,
            _commits,
            _slots
        );

        _replay = new AnalysisReplayService(
            Substitute.For<IRepository<RegressionBaseline, Guid>>(),
            _runs
        );
    }

    private ReconciliationAnalysisAppService CreateService()
    {
        return new ReconciliationAnalysisAppService(
            _analyses,
            _slots,
            _changes,
            _validations,
            _commits,
            _executions,
            _receipts,
            _profiles,
            _runs,
            _background,
            _replay,
            _commitService
        );
    }

    [Fact]
    public async Task StartOrResumeAsync_Reuses_Occupied_Slot_Without_Creating_New_Analysis()
    {
        var profileId = Guid.NewGuid();
        var existingAnalysisId = Guid.NewGuid();

        var slot = new ProfileAnalysisSlot(profileId)
        {
            ActiveAnalysisId = existingAnalysisId
        };

        _slots.FindAsync(profileId)
            .Returns(Task.FromResult<ProfileAnalysisSlot?>(slot));

        var existingAnalysis = new ReconciliationAnalysis(existingAnalysisId)
        {
            ProfileId = profileId,
            SourceRunId = Guid.NewGuid(),
            Status = AnalysisStatuses.AwaitingDecision
        };

        _analyses.FindAsync(existingAnalysisId)
            .Returns(Task.FromResult<ReconciliationAnalysis?>(existingAnalysis));
        _analyses.GetAsync(existingAnalysisId)
            .Returns(Task.FromResult(existingAnalysis));

        var service = CreateService();

        var input = new StartOrResumeAnalysisDto
        {
            RequestId = Guid.NewGuid(),
            ProfileId = profileId
        };

        var result = await service.StartOrResumeAsync(input);

        Assert.Equal(existingAnalysisId, result.AnalysisId);
        Assert.True(result.Reused);
        Assert.Equal(AnalysisStatuses.AwaitingDecision, result.Status);
        await _analyses.DidNotReceive().InsertAsync(Arg.Any<ReconciliationAnalysis>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task BatchProfileStatuses_Returns_NoRun_When_No_Successful_Runs_Exist()
    {
        var profileId = Guid.NewGuid();

        _profiles.FindAsync(profileId)
            .Returns(Task.FromResult<Profile?>(new Profile(profileId) { Name = "Profile A", Version = 1 }));

        _slots.FindAsync(profileId)
            .Returns(Task.FromResult<ProfileAnalysisSlot?>(null));

        _runs.GetQueryableAsync()
            .Returns(Task.FromResult(new List<ReconciliationRun>().AsQueryable()));

        var service = CreateService();

        var statuses = await service.GetProfileStatusesAsync(new BatchProfileStatusesInputDto
        {
            ProfileIds = [profileId]
        });

        var summary = Assert.Single(statuses);
        Assert.Equal(profileId, summary.ProfileId);
        Assert.Equal("no_run", summary.ActionType);
        Assert.False(summary.HasSuccessfulRun);
        Assert.NotNull(summary.Explanation);
    }

    [Fact]
    public async Task Advisor_Fallback_Generates_Deterministic_Advice_When_Model_Fails()
    {
        var failingGemini = Substitute.For<IGeminiClient>();
        failingGemini.GenerateJsonAsync(Arg.Any<string>())
            .Returns(Task.FromException<JsonElement>(new System.Net.Http.HttpRequestException("Gemini quota exceeded")));

        var advisor = new ReconciliationOptimizationAdvisor(failingGemini);

        var baseProfile = new ProfileDefinition
        {
            Name = "Test",
            Datasets = ["A", "B"],
            Rules =
            [
                new RuleDefinition
                {
                    Id = "R1",
                    Type = "exact",
                    Datasets = ["A", "B"],
                    Keys = [new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new() { ["A"] = "reference", ["B"] = "reference" } }]
                }
            ]
        };

        var manifest = new AnalysisEvidenceManifest
        {
            BaseUnresolvedRecords = 5,
            InitialDiagnoses =
            [
                new DiagnosisItemDto
                {
                    Category = "DateAmountDifferences",
                    Summary = "Unmatched records have exact reference match but date/amount variance",
                    AffectedRecordCount = 5
                }
            ],
            StratifiedEvidence =
            [
                new EvidenceRecord
                {
                    EvidenceId = "ev-1",
                    DatasetCode = "A",
                    RowIndex = 1,
                    Record = new() { ["reference"] = "TX100", ["amount"] = "500.00", ["date"] = "2026-08-10" },
                    CounterpartDatasetCode = "B",
                    CounterpartRowIndex = 1,
                    CounterpartRecord = new() { ["reference"] = "TX100", ["amount"] = "500.00", ["date"] = "2026-08-11" },
                    ObservedDifference = "date_divergence_1d"
                }
            ]
        };

        var rows = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["A"] = [new() { ["reference"] = "TX100", ["amount"] = "500.00", ["date"] = "2026-08-10" }],
            ["B"] = [new() { ["reference"] = "TX100", ["amount"] = "500.00", ["date"] = "2026-08-11" }]
        };

        var input = new OptimizationAdvisorInput
        {
            BaseProfile = baseProfile,
            Manifest = manifest,
            Rows = rows
        };

        var result = await advisor.AdviseAsync(input);

        Assert.Equal("deterministic", result.Source);
        Assert.NotEmpty(result.ChangeGroups);
        var op = Assert.Single(result.ChangeGroups[0].Operations);
        Assert.Equal("append_rule", op.OperationType);
        Assert.Equal("safe_additive", op.SafetyClass);
        Assert.True(op.IsSelectable);
        Assert.Contains("Recovers", op.Rationale);
    }

    [Fact]
    public async Task Advisor_Parses_Model_Response_When_OperationJson_Is_Native_Object()
    {
        var modelJson = """
        {
          "diagnoses": [
            {
              "category": "FormatOrToleranceDifferences",
              "summary": "Transactions have small date lag",
              "affectedRecordCount": 3,
              "sampleEvidenceIds": ["ev-1"]
            }
          ],
          "change_groups": [
            {
              "groupId": "group-1",
              "title": "Add date tolerance pass",
              "safetyClass": "safe_additive",
              "operations": [
                {
                  "operationId": "op-1",
                  "changeType": "profile",
                  "targetIdentifier": "PARTIAL_DATE_TOL",
                  "operationType": "append_rule",
                  "operationJson": {
                    "id": "PARTIAL_DATE_TOL",
                    "type": "partial",
                    "datasets": ["A", "B"],
                    "keys": [
                      { "type": "text", "match_mode": "exact", "fields": { "A": "reference", "B": "reference" } },
                      { "type": "amount", "match_mode": "exact", "fields": { "A": "amount", "B": "amount" } },
                      { "type": "date", "match_mode": "fuzzy", "algorithm": "date_proximity", "tolerance": "2", "min_score": 0.80, "fields": { "A": "date", "B": "date" } }
                    ],
                    "min_score": 0.80,
                    "remarks": "Date tolerance"
                  },
                  "rationale": "Matches date differences",
                  "safetyClass": "safe_additive"
                }
              ]
            }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(modelJson);
        var gemini = Substitute.For<IGeminiClient>();
        gemini.GenerateJsonAsync(Arg.Any<string>())
            .Returns(Task.FromResult(doc.RootElement.Clone()));

        var advisor = new ReconciliationOptimizationAdvisor(gemini);

        var baseProfile = new ProfileDefinition
        {
            Name = "Test",
            Datasets = ["A", "B"],
            Rules =
            [
                new RuleDefinition
                {
                    Id = "R1",
                    Type = "exact",
                    Datasets = ["A", "B"],
                    Keys = [new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new() { ["A"] = "reference", ["B"] = "reference" } }]
                }
            ]
        };

        var manifest = new AnalysisEvidenceManifest
        {
            BaseUnresolvedRecords = 3,
            StratifiedEvidence =
            [
                new EvidenceRecord { EvidenceId = "ev-1", DatasetCode = "A", RowIndex = 1, Reason = "Unmatched" }
            ]
        };

        var input = new OptimizationAdvisorInput
        {
            BaseProfile = baseProfile,
            Manifest = manifest,
            Rows = new() { ["A"] = [], ["B"] = [] }
        };

        var result = await advisor.AdviseAsync(input);

        Assert.Equal("model", result.Source);
        Assert.Single(result.ChangeGroups);
        var op = Assert.Single(result.ChangeGroups[0].Operations);
        Assert.Equal("PARTIAL_DATE_TOL", op.TargetIdentifier);
        Assert.Contains("PARTIAL_DATE_TOL", op.OperationJson);
    }

    [Fact]
    public void DeterministicAdvisor_Extracts_Keys_From_BaseProfile_Without_Heuristic_Guessing()
    {
        // Custom column names that do not match default substring heuristics
        var baseProfile = new ProfileDefinition
        {
            Name = "Arbitrary Col Profile",
            Datasets = ["ERP", "GATEWAY"],
            Rules =
            [
                new RuleDefinition
                {
                    Id = "EXACT_BASE",
                    Type = "exact",
                    Datasets = ["ERP", "GATEWAY"],
                    Keys =
                    [
                        new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new() { ["ERP"] = "belnr", ["GATEWAY"] = "ext_id" } },
                        new KeyDefinition { Type = "amount", MatchMode = "exact", Fields = new() { ["ERP"] = "wrbtr", ["GATEWAY"] = "charge" } },
                        new KeyDefinition { Type = "date", MatchMode = "exact", Fields = new() { ["ERP"] = "bldat", ["GATEWAY"] = "valuta" } }
                    ]
                }
            ]
        };

        var rows = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["ERP"] = [new() { ["belnr"] = "B100", ["wrbtr"] = "100.00", ["bldat"] = "2026-10-01" }],
            ["GATEWAY"] = [new() { ["ext_id"] = "B100", ["charge"] = "100.00", ["valuta"] = "2026-10-02" }]
        };

        var manifest = new AnalysisEvidenceManifest { BaseUnresolvedRecords = 2 };
        var input = new OptimizationAdvisorInput
        {
            BaseProfile = baseProfile,
            Manifest = manifest,
            Rows = rows
        };

        var result = ReconciliationOptimizationAdvisor.GenerateDeterministicAdvice(input);

        Assert.Equal(3, result.ChangeGroups.Count);
        var dateTol = result.ChangeGroups.FirstOrDefault(g => g.Operations.Any(o => o.TargetIdentifier == "PARTIAL_DATE_TOL"));
        Assert.NotNull(dateTol);

        var rule = JsonSerializer.Deserialize<RuleDefinition>(dateTol.Operations[0].OperationJson, ProfileJson.Options);
        Assert.NotNull(rule);

        var dateKey = rule.Keys.FirstOrDefault(k => k.Type == "date");
        Assert.NotNull(dateKey);
        Assert.Equal("bldat", dateKey.Fields["ERP"]);
        Assert.Equal("valuta", dateKey.Fields["GATEWAY"]);
    }
}
