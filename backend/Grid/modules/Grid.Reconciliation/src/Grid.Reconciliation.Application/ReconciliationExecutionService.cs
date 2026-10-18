using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Grid.Reconciliation;

public class ReconciliationExecutionService : ITransientDependency
{
    private readonly IRepository<ReconciliationRun, Guid> _runs;
    private readonly IRepository<MatchGroup, Guid> _groups;
    private readonly IRepository<RunRecord, Guid> _records;
    private readonly IRepository<ReconciliationRunSummary, Guid> _summaries;
    private readonly ReconciliationData _data;

    public ReconciliationExecutionService(
        IRepository<ReconciliationRun, Guid> runs,
        IRepository<MatchGroup, Guid> groups,
        IRepository<RunRecord, Guid> records,
        IRepository<ReconciliationRunSummary, Guid> summaries,
        ReconciliationData data)
    {
        _runs = runs;
        _groups = groups;
        _records = records;
        _summaries = summaries;
        _data = data;
    }

    public async Task<ReconciliationRun> ExecuteRunAsync(
        Profile profile,
        string definitionJson,
        Dictionary<string, List<Dictionary<string, string>>> rows,
        Guid? commitId = null,
        Guid? analysisId = null,
        Guid? executionId = null,
        Dictionary<string, string>? parserRunIds = null,
        Dictionary<string, List<Guid>>? multiParserRunIds = null)
    {
        var definition = ProfileJson.Parse(definitionJson);
        ReconciliationEngine.ValidateProfile(definition, rows);

        var result = ReconciliationEngine.Reconcile(rows, definition);

        var run = new ReconciliationRun
        {
            ProfileId = profile.Id,
            PackageId = profile.PackageId,
            ProfileVersion = profile.Version,
            Status = RunStatus.Succeeded,
            TotalRecords = result.Statistics.TotalRecords,
            MatchedRecords = result.Statistics.MatchedRecords,
            UnresolvedRecords = result.Statistics.UnresolvedRecords,
            AmbiguousRecords = result.Statistics.AmbiguousRecords,
            MatchedGroups = result.Statistics.MatchedGroups,
            DefinitionSnapshotJson = definitionJson,
            DatasetSnapshotJson = JsonSerializer.Serialize(rows),
        };

        if (commitId.HasValue) run.ExtraProperties["ConfigurationCommitId"] = commitId.Value.ToString();
        if (analysisId.HasValue) run.ExtraProperties["ReconciliationAnalysisId"] = analysisId.Value.ToString();
        if (executionId.HasValue) run.ExtraProperties["ConfigurationExecutionId"] = executionId.Value.ToString();
        if (parserRunIds != null) run.ExtraProperties["ParserRunIds"] = parserRunIds;
        if (multiParserRunIds != null) run.ExtraProperties["MultiParserRunIds"] = multiParserRunIds;

        await _runs.InsertAsync(run, autoSave: true);
        await _data.EnsureBaselineAsync(profile.PackageId, definitionJson, rows);

        var groups = result.MatchedGroups.Select((group, index) => new MatchGroup
        {
            RunId = run.Id,
            Sequence = index,
            RuleId = group.Rule,
            KeyJson = JsonSerializer.Serialize(group.Key),
            RecordsJson = JsonSerializer.Serialize(group.Records),
            EvidenceJson = JsonSerializer.Serialize(group.Evidence, ProfileJson.Storage),
        }).ToList();

        var unresolved = result.Unresolved.Select(row => new RunRecord
        {
            RunId = run.Id,
            DatasetCode = row.Dataset,
            CsvRow = row.CsvRow,
            Reason = row.Reason,
            RecordJson = JsonSerializer.Serialize(row.Record),
        }).ToList();

        if (groups.Count > 0)
        {
            await _groups.InsertManyAsync(groups, autoSave: true);
        }

        if (unresolved.Count > 0)
        {
            await _records.InsertManyAsync(unresolved, autoSave: true);
        }

        await _summaries.InsertAsync(ReconciliationRunSummary.From(run, result), autoSave: true);

        return run;
    }
}
