using System.Text.Json;
using System.Text.RegularExpressions;
using Grid;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Grid.Reconciliation;

[Authorize]
public class DatasetAppService : GridAppService, IDatasetAppService
{
    private static readonly Regex CodePattern = new("^[A-Za-z][A-Za-z0-9_]{0,39}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IRepository<Dataset, Guid> _datasets;
    private readonly IRepository<DatasetRow, Guid> _rows;

    public DatasetAppService(IRepository<Dataset, Guid> datasets, IRepository<DatasetRow, Guid> rows)
    {
        _datasets = datasets;
        _rows = rows;
    }

    public async Task<List<DatasetDto>> GetListAsync()
    {
        var datasets = await _datasets.GetListAsync();
        return datasets
            .OrderBy(dataset => dataset.Code, StringComparer.Ordinal)
            .Select(dataset => Map(dataset, null))
            .ToList();
    }

    public async Task<DatasetDto> GetAsync(Guid id)
    {
        var dataset = await _datasets.GetAsync(id);
        var sample = await SampleAsync(dataset.Id);
        return Map(dataset, sample);
    }

    public async Task<DatasetDto> CreateAsync(CreateDatasetDto input)
    {
        var code = (input.Code ?? "").Trim();
        if (!CodePattern.IsMatch(code))
        {
            throw new UserFriendlyException("Dataset code must start with a letter and use only letters, numbers, and underscores.");
        }

        List<Dictionary<string, string>> parsed;
        try
        {
            parsed = ReconciliationEngine.ReadCsv(input.CsvText ?? "");
        }
        catch (ReconciliationException ex)
        {
            throw new UserFriendlyException(ex.Message);
        }

        if (parsed.Count == 0)
        {
            throw new UserFriendlyException("Each CSV must contain at least one data row");
        }

        var columns = parsed[0].Keys.Select(name => new DatasetColumnDto
        {
            Name = name,
            Type = ReconciliationEngine.InferColumnType(parsed.Select(row => row[name])),
        }).ToList();

        var existing = await _datasets.FirstOrDefaultAsync(dataset => dataset.Code == code);
        Dataset dataset;
        if (existing == null)
        {
            dataset = new Dataset
            {
                Code = code,
                Name = string.IsNullOrWhiteSpace(input.Name) ? code : input.Name.Trim(),
                ColumnsJson = JsonSerializer.Serialize(columns, Json),
                RowCount = parsed.Count,
            };
            await _datasets.InsertAsync(dataset, autoSave: true);
        }
        else
        {
            await _rows.DeleteAsync(row => row.DatasetId == existing.Id, autoSave: true);
            existing.Name = string.IsNullOrWhiteSpace(input.Name) ? existing.Name : input.Name.Trim();
            existing.ColumnsJson = JsonSerializer.Serialize(columns, Json);
            existing.RowCount = parsed.Count;
            await _datasets.UpdateAsync(existing, autoSave: true);
            dataset = existing;
        }

        var entities = parsed.Select((row, index) => new DatasetRow
        {
            DatasetId = dataset.Id,
            RowNumber = index + 2,
            ValuesJson = JsonSerializer.Serialize(row),
        }).ToList();
        await _rows.InsertManyAsync(entities, autoSave: true);

        return Map(dataset, parsed.Take(5).Select(row => new Dictionary<string, string>(row)).ToList());
    }

    public async Task<DatasetDto> UpdateAsync(Guid id, UpdateDatasetDto input)
    {
        var dataset = await _datasets.GetAsync(id);
        var current = JsonSerializer.Deserialize<List<DatasetColumnDto>>(dataset.ColumnsJson, Json) ?? [];
        if (input.Columns == null || input.Columns.Count != current.Count ||
            input.Columns.Select(column => column.Name).SequenceEqual(current.Select(column => column.Name), StringComparer.Ordinal) == false)
        {
            throw new UserFriendlyException("Column names come from the uploaded file and cannot be renamed.");
        }

        foreach (var column in input.Columns)
        {
            if (column.Type is not ("text" or "amount" or "date"))
            {
                throw new UserFriendlyException("Column type must be text, amount, or date.");
            }
        }

        dataset.ColumnsJson = JsonSerializer.Serialize(input.Columns, Json);
        await _datasets.UpdateAsync(dataset, autoSave: true);
        return Map(dataset, await SampleAsync(dataset.Id));
    }

    public async Task DeleteAsync(Guid id)
    {
        await _rows.DeleteAsync(row => row.DatasetId == id);
        await _datasets.DeleteAsync(id);
    }

    private async Task<List<Dictionary<string, string>>> SampleAsync(Guid datasetId)
    {
        var query = await _rows.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(query.Where(row => row.DatasetId == datasetId).OrderBy(row => row.RowNumber).Take(5));
        return rows.Select(ReadRow).ToList();
    }

    private static DatasetDto Map(Dataset dataset, List<Dictionary<string, string>>? sample)
    {
        return new DatasetDto
        {
            Id = dataset.Id,
            Code = dataset.Code,
            Name = dataset.Name,
            RowCount = dataset.RowCount,
            Columns = JsonSerializer.Deserialize<List<DatasetColumnDto>>(dataset.ColumnsJson, Json) ?? [],
            SampleRows = sample,
        };
    }

    internal static Dictionary<string, string> ReadRow(DatasetRow row)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(row.ValuesJson) ?? [];
    }
}

[Authorize]
public class PackageAppService : GridAppService, IPackageAppService
{
    private readonly IRepository<Package, Guid> _packages;
    private readonly IRepository<Profile, Guid> _profiles;

    public PackageAppService(IRepository<Package, Guid> packages, IRepository<Profile, Guid> profiles)
    {
        _packages = packages;
        _profiles = profiles;
    }

    public async Task<List<PackageDto>> GetListAsync()
    {
        var packages = await _packages.GetListAsync();
        var profiles = await _profiles.GetListAsync();
        return packages
            .OrderByDescending(package => package.CreationTime)
            .Select(package => Map(package, profiles.First(profile => profile.PackageId == package.Id)))
            .ToList();
    }

    public async Task<PackageDto> GetAsync(Guid id)
    {
        var package = await _packages.GetAsync(id);
        var profile = await _profiles.GetAsync(item => item.PackageId == id);
        return Map(package, profile);
    }

    public async Task<PackageDto> CreateAsync(CreatePackageDto input)
    {
        var name = (input.Name ?? "").Trim();
        if (name.Length is < 1 or > 128)
        {
            throw new UserFriendlyException("Package name is required.");
        }

        var package = new Package
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
        };
        await _packages.InsertAsync(package, autoSave: true);

        var definition = new ProfileDefinition
        {
            Name = name,
            Version = 0,
            Datasets = [],
            Rules = [],
        };
        var profile = new Profile
        {
            PackageId = package.Id,
            Name = name,
            Version = 0,
            Status = ProfileStatus.Draft,
            DefinitionJson = ProfileJson.Write(definition),
        };
        await _profiles.InsertAsync(profile, autoSave: true);
        return Map(package, profile);
    }

    private static PackageDto Map(Package package, Profile profile)
    {
        return new PackageDto
        {
            Id = package.Id,
            Name = package.Name,
            Description = package.Description,
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            ProfileVersion = profile.Version,
            ProfileStatus = profile.Status.ToString(),
            CreationTime = package.CreationTime,
        };
    }
}

[Authorize]
public class ProfileAppService : GridAppService, IProfileAppService
{
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IRepository<Package, Guid> _packages;
    private readonly IRepository<RegressionBaseline, Guid> _baselines;
    private readonly ReconciliationData _data;

    public ProfileAppService(
        IRepository<Profile, Guid> profiles,
        IRepository<Package, Guid> packages,
        IRepository<RegressionBaseline, Guid> baselines,
        ReconciliationData data)
    {
        _profiles = profiles;
        _packages = packages;
        _baselines = baselines;
        _data = data;
    }

    public async Task<ProfileDto> GetAsync(Guid id)
    {
        var profile = await _profiles.GetAsync(id);
        var package = await _packages.GetAsync(profile.PackageId);
        var hasBaseline = await _baselines.AnyAsync(item => item.PackageId == profile.PackageId);
        return Map(profile, package, hasBaseline);
    }

    public async Task<ProfileDto> UpdateAsync(Guid id, SaveProfileDto input)
    {
        var profile = await _profiles.GetAsync(id);
        var definition = Parse(input.DefinitionJson);
        profile.DefinitionJson = ProfileJson.Write(definition);
        profile.Name = string.IsNullOrWhiteSpace(definition.Name) ? profile.Name : definition.Name.Trim();
        if (profile.Status == ProfileStatus.Approved)
        {
            profile.Status = ProfileStatus.Draft;
        }

        await _profiles.UpdateAsync(profile, autoSave: true);
        return await GetAsync(id);
    }

    public async Task<ProfileDto> ApproveAsync(Guid id, SaveProfileDto input)
    {
        var profile = await _profiles.GetAsync(id);
        var definition = Parse(input.DefinitionJson);
        Dictionary<string, List<Dictionary<string, string>>> rows;
        try
        {
            rows = await _data.LoadAsync(definition.Datasets);
            ReconciliationEngine.ValidateProfile(definition, rows);
            await _data.EnsureRegressionAsync(profile.PackageId, definition);
        }
        catch (ReconciliationException ex)
        {
            throw new UserFriendlyException(ex.Message);
        }

        definition.Version = profile.Version + 1;
        var json = ProfileJson.Write(definition);
        profile.DefinitionJson = json;
        profile.ApprovedDefinitionJson = json;
        profile.Version = definition.Version;
        profile.Name = string.IsNullOrWhiteSpace(definition.Name) ? profile.Name : definition.Name.Trim();
        profile.Status = ProfileStatus.Approved;
        await _profiles.UpdateAsync(profile, autoSave: true);

        if (!await _baselines.AnyAsync(item => item.PackageId == profile.PackageId))
        {
            await _baselines.InsertAsync(new RegressionBaseline
            {
                PackageId = profile.PackageId,
                DatasetsJson = JsonSerializer.Serialize(rows),
                ProfileJson = json,
            }, autoSave: true);
        }

        return await GetAsync(id);
    }

    private static ProfileDefinition Parse(string json)
    {
        try
        {
            return ProfileJson.Parse(json ?? "");
        }
        catch (ReconciliationException ex)
        {
            throw new UserFriendlyException(ex.Message);
        }
    }

    private static ProfileDto Map(Profile profile, Package package, bool hasBaseline)
    {
        return new ProfileDto
        {
            Id = profile.Id,
            PackageId = profile.PackageId,
            PackageName = package.Name,
            Name = profile.Name,
            Version = profile.Version,
            Status = profile.Status.ToString(),
            DefinitionJson = profile.DefinitionJson,
            ApprovedDefinitionJson = profile.ApprovedDefinitionJson,
            HasBaseline = hasBaseline,
        };
    }
}

public class ReconciliationData : ITransientDependency
{
    private readonly IRepository<Dataset, Guid> _datasets;
    private readonly IRepository<DatasetRow, Guid> _rows;
    private readonly IRepository<RegressionBaseline, Guid> _baselines;

    public ReconciliationData(
        IRepository<Dataset, Guid> datasets,
        IRepository<DatasetRow, Guid> rows,
        IRepository<RegressionBaseline, Guid> baselines)
    {
        _datasets = datasets;
        _rows = rows;
        _baselines = baselines;
    }

    public async Task<Dictionary<string, List<Dictionary<string, string>>>> LoadAsync(IEnumerable<string> codes)
    {
        var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var code in codes.Distinct(StringComparer.Ordinal))
        {
            var dataset = await _datasets.FirstOrDefaultAsync(item => item.Code == code);
            if (dataset == null)
            {
                throw new UserFriendlyException($"Upload dataset {code} before using it in a profile.");
            }

            var stored = await _rows.GetListAsync(row => row.DatasetId == dataset.Id);
            result[code] = stored.OrderBy(row => row.RowNumber).Select(DatasetAppService.ReadRow).ToList();
        }

        return result;
    }

    public async Task EnsureRegressionAsync(Guid packageId, ProfileDefinition proposed)
    {
        var baseline = await _baselines.FirstOrDefaultAsync(item => item.PackageId == packageId);
        if (baseline == null)
        {
            return;
        }

        var rows = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(baseline.DatasetsJson)
            ?? throw new ReconciliationException("The original batch snapshot is unreadable.");
        var original = ProfileJson.Parse(baseline.ProfileJson);
        var previous = ReconciliationEngine.Reconcile(rows, original);
        var next = ReconciliationEngine.Reconcile(rows, proposed);
        if (!ReconciliationEngine.PreservesMatches(previous, next))
        {
            throw new ReconciliationException("Regression check failed: previously matched groups changed. Revision cannot be approved.");
        }
    }
}

[Authorize]
public class RunAppService : GridAppService, IRunAppService
{
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IRepository<ReconciliationRun, Guid> _runs;
    private readonly IRepository<MatchGroup, Guid> _groups;
    private readonly IRepository<RunRecord, Guid> _records;
    private readonly ReconciliationData _data;

    public RunAppService(
        IRepository<Profile, Guid> profiles,
        IRepository<ReconciliationRun, Guid> runs,
        IRepository<MatchGroup, Guid> groups,
        IRepository<RunRecord, Guid> records,
        ReconciliationData data)
    {
        _profiles = profiles;
        _runs = runs;
        _groups = groups;
        _records = records;
        _data = data;
    }

    public async Task<List<RunDto>> GetListAsync(RunListInput input)
    {
        var query = await _runs.GetQueryableAsync();
        var runs = await AsyncExecuter.ToListAsync(query.Where(run => run.ProfileId == input.ProfileId).OrderByDescending(run => run.CreationTime));
        return runs.Select(MapSummary).ToList();
    }

    public async Task<RunDetailDto> GetAsync(Guid id)
    {
        var run = await _runs.GetAsync(id);
        var groups = await _groups.GetListAsync(group => group.RunId == id);
        var records = await _records.GetListAsync(record => record.RunId == id);
        return MapDetail(run, groups.OrderBy(group => group.Sequence).ToList(), records.OrderBy(record => record.DatasetCode).ThenBy(record => record.CsvRow).ToList());
    }

    public async Task<RunDetailDto> CreateAsync(CreateRunDto input)
    {
        var profile = await _profiles.GetAsync(input.ProfileId);
        if (profile.Status != ProfileStatus.Approved || string.IsNullOrWhiteSpace(profile.ApprovedDefinitionJson))
        {
            throw new UserFriendlyException("Approve the profile before running reconciliation.");
        }

        var definition = ProfileJson.Parse(profile.ApprovedDefinitionJson);
        Dictionary<string, List<Dictionary<string, string>>> rows;
        ReconcileResult result;
        try
        {
            rows = await _data.LoadAsync(definition.Datasets);
            result = ReconciliationEngine.Reconcile(rows, definition);
        }
        catch (ReconciliationException ex)
        {
            throw new UserFriendlyException(ex.Message);
        }

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
            DefinitionSnapshotJson = profile.ApprovedDefinitionJson,
            DatasetSnapshotJson = JsonSerializer.Serialize(rows),
        };
        await _runs.InsertAsync(run, autoSave: true);

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

        return MapDetail(run, groups, unresolved);
    }

    private static RunDto MapSummary(ReconciliationRun run)
    {
        return new RunDto
        {
            Id = run.Id,
            ProfileId = run.ProfileId,
            ProfileVersion = run.ProfileVersion,
            Status = run.Status.ToString(),
            CreationTime = run.CreationTime,
            TotalRecords = run.TotalRecords,
            MatchedRecords = run.MatchedRecords,
            UnresolvedRecords = run.UnresolvedRecords,
            AmbiguousRecords = run.AmbiguousRecords,
            MatchedGroups = run.MatchedGroups,
        };
    }

    private static RunDetailDto MapDetail(ReconciliationRun run, List<MatchGroup> groups, List<RunRecord> records)
    {
        var detail = new RunDetailDto
        {
            Id = run.Id,
            ProfileId = run.ProfileId,
            ProfileVersion = run.ProfileVersion,
            Status = run.Status.ToString(),
            CreationTime = run.CreationTime,
            TotalRecords = run.TotalRecords,
            MatchedRecords = run.MatchedRecords,
            UnresolvedRecords = run.UnresolvedRecords,
            AmbiguousRecords = run.AmbiguousRecords,
            MatchedGroups = run.MatchedGroups,
            Groups = groups.Select(group => new MatchGroupDto
            {
                Rule = group.RuleId,
                Key = JsonSerializer.Deserialize<List<string>>(group.KeyJson) ?? [],
                Records = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(group.RecordsJson) ?? [],
                Evidence = JsonSerializer.Deserialize<EvidenceDto>(group.EvidenceJson, ProfileJson.Storage) ?? new EvidenceDto(),
            }).ToList(),
            Unresolved = records.Select(record => new UnresolvedRowDto
            {
                Dataset = record.DatasetCode,
                CsvRow = record.CsvRow,
                Reason = record.Reason,
                Record = JsonSerializer.Deserialize<Dictionary<string, string>>(record.RecordJson) ?? [],
            }).ToList(),
        };
        return detail;
    }
}
