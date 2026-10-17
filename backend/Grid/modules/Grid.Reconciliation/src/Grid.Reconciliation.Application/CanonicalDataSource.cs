using System.Text.Json;
using Grid.Datasets;
using Grid.Parsers;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Grid.Reconciliation;

/// <summary>Loads the immutable output of a successful parser run, never the raw upload.</summary>
public class CanonicalDataSource : ITransientDependency
{
    private readonly IRepository<CanonicalDataset, Guid> _datasets;
    private readonly IRepository<ParserRun, Guid> _runs;
    private readonly ICanonicalOutputReader _output;

    public CanonicalDataSource(IRepository<CanonicalDataset, Guid> datasets, IRepository<ParserRun, Guid> runs, ICanonicalOutputReader output)
    {
        _datasets = datasets;
        _runs = runs;
        _output = output;
    }

    public async Task<List<DatasetDto>> GetCatalogAsync()
    {
        var datasets = await _datasets.GetListAsync();
        var runs = await _runs.GetListAsync(run => run.Status == "Succeeded");
        return datasets.Select(dataset => new DatasetDto
        {
            Id = dataset.Id,
            Code = dataset.Code,
            Name = dataset.Name,
            Columns = Columns(dataset),
            RowCount = ReadRowCount(runs.Where(run => DatasetId(run) == dataset.Id).OrderByDescending(run => run.CreationTime).FirstOrDefault()),
        }).OrderBy(dataset => dataset.Code, StringComparer.Ordinal).ToList();
    }

    public async Task<Dictionary<string, string>?> GetSchemaAsync(string code)
    {
        var dataset = await _datasets.FindAsync(item => item.Code == code);
        return dataset == null ? null : Columns(dataset).ToDictionary(column => column.Name, _ => "", StringComparer.Ordinal);
    }

    public Task<List<Dictionary<string, string>>?> LoadAsync(string code, Guid? runId, bool requireExplicitRun = false)
    {
        return LoadMultipleAsync(code, runId.HasValue ? new[] { runId.Value } : null, requireExplicitRun);
    }

    public async Task<List<Dictionary<string, string>>?> LoadMultipleAsync(string code, IEnumerable<Guid>? runIds, bool requireExplicitRun = false)
    {
        var dataset = await _datasets.FindAsync(item => item.Code == code);
        if (dataset == null) return null;

        var idList = runIds?.Distinct().ToList();
        if (requireExplicitRun && (idList == null || idList.Count == 0))
            throw new UserFriendlyException($"Select a processed file for {code} before starting this run.");

        List<ParserRun> runsToLoad = [];
        if (idList != null && idList.Count > 0)
        {
            foreach (var id in idList)
            {
                var run = await _runs.GetAsync(id);
                if (DatasetId(run) != dataset.Id || run.Status != "Succeeded")
                    throw new UserFriendlyException($"Select a successful parser run for dataset {code}.");
                runsToLoad.Add(run);
            }
        }
        else
        {
            var candidates = await _runs.GetListAsync(item => item.Status == "Succeeded");
            var candidate = candidates.Where(item => DatasetId(item) == dataset.Id).OrderByDescending(item => item.CreationTime).FirstOrDefault();
            if (candidate != null) runsToLoad.Add(candidate);
        }

        if (runsToLoad.Count == 0)
            throw new UserFriendlyException($"Upload a file through a published parser for {code} before running reconciliation.");

        var combinedRows = new List<Dictionary<string, string>>();
        var expected = Columns(dataset).Select(column => column.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var run in runsToLoad)
        {
            if (run.ExtraProperties.TryGetValue("SchemaColumnsJson", out var snapshot) &&
                DatasetContractValidator.TryReadContract(snapshot?.ToString() ?? "", out var originalFields) &&
                DatasetContractValidator.TryReadContract(dataset.SchemaColumnsJson, out var currentFields) &&
                !originalFields.OrderBy(field => field.Name, StringComparer.Ordinal).SequenceEqual(currentFields.OrderBy(field => field.Name, StringComparer.Ordinal)))
                throw new UserFriendlyException($"The dataset contract for {code} changed. Reprocess this file before using it in a new run.");

            // The sidecar is produced from the same canonical frame as the Parquet object.
            // Its path comes from the persisted run, never from a client-supplied URI.
            if (!run.OutputObject.StartsWith("gs://", StringComparison.Ordinal))
                throw new UserFriendlyException($"Parser run {run.Id} has no Cloud Storage output.");

            var rows = await _output.ReadAsync(run.OutputObject + ".csv");
            if (rows.Count > 0)
            {
                if (!expected.SetEquals(rows[0].Keys))
                    throw new UserFriendlyException($"The output schema for {code} changed. Reprocess the file using the current dataset contract.");
                combinedRows.AddRange(rows);
            }
        }

        if (combinedRows.Count == 0) throw new UserFriendlyException($"Parser run for {code} produced no usable rows.");
        return combinedRows;
    }

    private static List<DatasetColumnDto> Columns(CanonicalDataset dataset)
    {
        if (!DatasetContractValidator.TryReadContract(dataset.SchemaColumnsJson, out var fields))
            throw new UserFriendlyException($"Dataset {dataset.Code} has an invalid schema.");
        return fields.Select(field => new DatasetColumnDto
        {
            Name = field.Name,
            Type = field.Type is "decimal" or "float" or "integer" or "int32" or "int64" ? "amount" : field.Type is "date" or "datetime" ? "date" : "text",
        }).ToList();
    }

    private static Guid? DatasetId(ParserRun run) =>
        run.ExtraProperties.TryGetValue("DatasetId", out var value) && Guid.TryParse(value?.ToString(), out var id) ? id : null;

    private static int ReadRowCount(ParserRun? run)
    {
        if (string.IsNullOrWhiteSpace(run?.ReportJson)) return 0;
        try
        {
            using var report = JsonDocument.Parse(run.ReportJson);
            return report.RootElement.TryGetProperty("output_rows", out var rows) && rows.TryGetInt32(out var count) ? count : 0;
        }
        catch (JsonException) { return 0; }
    }
}
