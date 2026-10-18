using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Grid.Datasets;
using Grid.Parsers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Grid.Reconciliation;

[Authorize]
public class DatasetAppService : ApplicationService, IDatasetAppService
{
    private static readonly Regex CodePattern = new("^[A-Za-z][A-Za-z0-9_]{0,39}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IRepository<Dataset, Guid> _datasets;
    private readonly IRepository<DatasetRow, Guid> _rows;
    private readonly CanonicalDataSource _canonical;

    public DatasetAppService(IRepository<Dataset, Guid> datasets, IRepository<DatasetRow, Guid> rows, CanonicalDataSource canonical)
    {
        _datasets = datasets;
        _rows = rows;
        _canonical = canonical;
    }

    public async Task<List<DatasetDto>> GetListAsync()
    {
        return await _canonical.GetCatalogAsync();
    }

    public async Task<DatasetDto> GetAsync(Guid id)
    {
        var canonical = (await _canonical.GetCatalogAsync()).FirstOrDefault(dataset => dataset.Id == id);
        if (canonical != null) return canonical;
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
public class PackageAppService : ApplicationService, IPackageAppService
{
    private readonly IRepository<Package, Guid> _packages;
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IRepository<RegressionBaseline, Guid> _baselines;
    private readonly IRepository<ReconciliationRun, Guid> _runs;
    private readonly IRepository<MatchGroup, Guid> _groups;
    private readonly IRepository<RunRecord, Guid> _records;

    public PackageAppService(
        IRepository<Package, Guid> packages,
        IRepository<Profile, Guid> profiles,
        IRepository<RegressionBaseline, Guid> baselines,
        IRepository<ReconciliationRun, Guid> runs,
        IRepository<MatchGroup, Guid> groups,
        IRepository<RunRecord, Guid> records)
    {
        _packages = packages;
        _profiles = profiles;
        _baselines = baselines;
        _runs = runs;
        _groups = groups;
        _records = records;
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
        if (input.CreatedWithAi)
        {
            profile.ExtraProperties["CreatedWithAi"] = true;
        }
        await _profiles.InsertAsync(profile, autoSave: true);
        return Map(package, profile);
    }

    public async Task DeleteAsync(Guid id)
    {
        var profile = await _profiles.FirstOrDefaultAsync(p => p.PackageId == id);
        await CascadeDeletePackageAsync(id, profile?.Id, _packages, _profiles, _baselines, _runs, _groups, _records);
    }

    internal static async Task CascadeDeletePackageAsync(
        Guid packageId,
        Guid? profileId,
        IRepository<Package, Guid> packages,
        IRepository<Profile, Guid> profiles,
        IRepository<RegressionBaseline, Guid> baselines,
        IRepository<ReconciliationRun, Guid> runs,
        IRepository<MatchGroup, Guid> groups,
        IRepository<RunRecord, Guid> records)
    {
        var runList = await runs.GetListAsync(r => r.PackageId == packageId || (profileId.HasValue && r.ProfileId == profileId.Value));
        if (runList.Count > 0)
        {
            var runIds = runList.Select(r => r.Id).ToList();
            await records.DeleteAsync(r => runIds.Contains(r.RunId), autoSave: true);
            await groups.DeleteAsync(g => runIds.Contains(g.RunId), autoSave: true);
            await runs.DeleteAsync(r => runIds.Contains(r.Id), autoSave: true);
        }
        await baselines.DeleteAsync(b => b.PackageId == packageId, autoSave: true);
        await profiles.DeleteAsync(p => p.PackageId == packageId || (profileId.HasValue && p.Id == profileId.Value), autoSave: true);
        await packages.DeleteAsync(p => p.Id == packageId, autoSave: true);
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
            CreatedWithAi = MarkedByAi(profile.ExtraProperties),
        };
    }

    private static bool MarkedByAi(IDictionary<string, object?> values)
    {
        if (!values.TryGetValue("CreatedWithAi", out var value) || value is null) return false;
        return value switch
        {
            bool flag => flag,
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            _ => bool.TryParse(value.ToString(), out var parsed) && parsed
        };
    }
}

[Authorize]
public class ProfileAppService : ApplicationService, IProfileAppService
{
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IRepository<Package, Guid> _packages;
    private readonly IRepository<RegressionBaseline, Guid> _baselines;
    private readonly IRepository<ReconciliationRun, Guid> _runs;
    private readonly IRepository<MatchGroup, Guid> _groups;
    private readonly IRepository<RunRecord, Guid> _records;
    private readonly ReconciliationData _data;
    private IRepository<CanonicalDataset, Guid>? _canonicalDatasets;
    private IRepository<ParserConfiguration, Guid>? _parsers;

    public ProfileAppService(
        IRepository<Profile, Guid> profiles,
        IRepository<Package, Guid> packages,
        IRepository<RegressionBaseline, Guid> baselines,
        IRepository<ReconciliationRun, Guid> runs,
        IRepository<MatchGroup, Guid> groups,
        IRepository<RunRecord, Guid> records,
        ReconciliationData data,
        IRepository<CanonicalDataset, Guid>? canonicalDatasets = null,
        IRepository<ParserConfiguration, Guid>? parsers = null)
    {
        _profiles = profiles;
        _packages = packages;
        _baselines = baselines;
        _runs = runs;
        _groups = groups;
        _records = records;
        _data = data;
        _canonicalDatasets = canonicalDatasets;
        _parsers = parsers;
    }

    private IRepository<CanonicalDataset, Guid> CanonicalDatasets =>
        _canonicalDatasets ??= LazyServiceProvider.LazyGetRequiredService<IRepository<CanonicalDataset, Guid>>();
    private IRepository<ParserConfiguration, Guid> Parsers =>
        _parsers ??= LazyServiceProvider.LazyGetRequiredService<IRepository<ParserConfiguration, Guid>>();

    public async Task DeleteAsync(Guid id)
    {
        var profile = await _profiles.FindAsync(id);
        var packageId = profile?.PackageId ?? id;
        await PackageAppService.CascadeDeletePackageAsync(packageId, id, _packages, _profiles, _baselines, _runs, _groups, _records);
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

        if (input.CreatedWithAi)
        {
            profile.ExtraProperties["CreatedWithAi"] = true;
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
            rows = await _data.LoadSchemasAsync(definition.Datasets);
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
        if (input.CreatedWithAi)
        {
            profile.ExtraProperties["CreatedWithAi"] = true;
        }
        await _profiles.UpdateAsync(profile, autoSave: true);

        return await GetAsync(id);
    }

    public async Task<ProfileBundleDto> GetBundleAsync(Guid id)
    {
        var profile = await _profiles.GetAsync(id);
        var package = await _packages.GetAsync(profile.PackageId);
        var definition = Parse(profile.DefinitionJson);

        var bundle = new ProfileBundleDto
        {
            FormatVersion = 1,
            ExportedAt = DateTime.UtcNow,
            Package = new BundlePackageDto
            {
                Name = package.Name,
                Description = package.Description,
            },
            Profile = new BundleProfileDto
            {
                Name = profile.Name,
                Description = package.Description,
                Version = profile.Version,
                Status = profile.Status.ToString(),
                DefinitionJson = profile.DefinitionJson,
                CreatedWithAi = MarkedByAi(profile.ExtraProperties),
            },
            Datasets = [],
            Parsers = [],
        };

        var allDatasets = await CanonicalDatasets.GetListAsync();
        var allParsers = await Parsers.GetListAsync();

        var referencedDatasets = new HashSet<string>(definition.Datasets ?? [], StringComparer.OrdinalIgnoreCase);

        var matchingCanonicalDatasets = allDatasets
            .Where(d => referencedDatasets.Contains(d.Code))
            .ToList();

        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ds in matchingCanonicalDatasets)
        {
            bundle.Datasets.Add(new BundleDatasetDto
            {
                Code = ds.Code,
                Name = ds.Name,
                Description = ds.Description,
                GcsPrefix = ds.GcsPrefix,
                SchemaColumnsJson = ds.SchemaColumnsJson,
                PartitionColumnsJson = ds.PartitionColumnsJson,
            });

            if (!string.IsNullOrWhiteSpace(ds.GcsPrefix))
            {
                prefixes.Add(NormalizePrefix(ds.GcsPrefix));
            }
        }

        foreach (var parser in allParsers)
        {
            var parserPrefix = NormalizePrefix(parser.OutputGcsPrefix);
            var studioId = TryGetStudioDatasetId(parser.PipelineJson);

            var matchesDataset = matchingCanonicalDatasets.FirstOrDefault(ds =>
                (!string.IsNullOrWhiteSpace(parserPrefix) && NormalizePrefix(ds.GcsPrefix) == parserPrefix) ||
                (studioId.HasValue && ds.Id == studioId.Value) ||
                parser.Name.Replace(" ", "").Replace("_", "").Contains(ds.Code.Replace(" ", "").Replace("_", ""), StringComparison.OrdinalIgnoreCase) ||
                ds.Code.Replace(" ", "").Replace("_", "").Contains(parser.Name.Replace(" ", "").Replace("_", ""), StringComparison.OrdinalIgnoreCase)
            );

            if (matchesDataset != null)
            {
                var outputPrefix = !string.IsNullOrWhiteSpace(parser.OutputGcsPrefix)
                    ? parser.OutputGcsPrefix
                    : matchesDataset.GcsPrefix;

                bundle.Parsers.Add(new BundleParserDto
                {
                    Name = parser.Name,
                    FileFormat = parser.FileFormat,
                    PipelineJson = parser.PipelineJson,
                    OutputGcsPrefix = outputPrefix,
                    IsActive = parser.IsActive,
                    CreatedWithAi = MarkedByAi(parser.ExtraProperties),
                });
            }
        }

        return bundle;
    }

    public async Task<ProfileDto> ImportBundleAsync(ProfileBundleDto input)
    {
        if (input == null)
        {
            throw new UserFriendlyException("Bundle payload cannot be null.");
        }

        input.Profile ??= new BundleProfileDto();

        var definitionJson = input.Profile.DefinitionJson;
        if ((string.IsNullOrWhiteSpace(definitionJson) || definitionJson == "{}") &&
            input.Profile.ExtraProperties.TryGetValue("definition", out var defEl) &&
            defEl.ValueKind == JsonValueKind.Object)
        {
            definitionJson = defEl.GetRawText();
        }

        var definition = Parse(definitionJson);

        var profileName = !string.IsNullOrWhiteSpace(input.Profile.Name)
            ? input.Profile.Name.Trim()
            : !string.IsNullOrWhiteSpace(definition.Name)
                ? definition.Name.Trim()
                : !string.IsNullOrWhiteSpace(input.Package?.Name)
                    ? input.Package.Name.Trim()
                    : "Imported Profile";

        definition.Name = profileName;

        // Upsert Datasets
        if (input.Datasets != null && input.Datasets.Count > 0)
        {
            var existingDatasets = await CanonicalDatasets.GetListAsync();
            foreach (var dsDto in input.Datasets)
            {
                var code = (dsDto.Code ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(code)) continue;

                var schemaJson = dsDto.SchemaColumnsJson;
                if ((string.IsNullOrWhiteSpace(schemaJson) || schemaJson == "[]") &&
                    dsDto.ExtraProperties.TryGetValue("schemaColumns", out var schemaEl) &&
                    schemaEl.ValueKind == JsonValueKind.Array)
                {
                    schemaJson = schemaEl.GetRawText();
                }

                var partitionJson = dsDto.PartitionColumnsJson;
                if ((string.IsNullOrWhiteSpace(partitionJson) || partitionJson == "[]") &&
                    dsDto.ExtraProperties.TryGetValue("partitionColumns", out var partEl) &&
                    partEl.ValueKind == JsonValueKind.Array)
                {
                    partitionJson = partEl.GetRawText();
                }

                var name = string.IsNullOrWhiteSpace(dsDto.Name) ? code : dsDto.Name.Trim();
                var description = dsDto.Description?.Trim() ?? "";
                var gcsPrefix = string.IsNullOrWhiteSpace(dsDto.GcsPrefix) ? $"canonical/{code.ToLowerInvariant()}" : dsDto.GcsPrefix.Trim().Trim('/').ToLowerInvariant();

                var match = existingDatasets.FirstOrDefault(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    match.Name = name;
                    match.Description = description;
                    if (!string.IsNullOrWhiteSpace(schemaJson) && schemaJson != "[]")
                    {
                        match.SchemaColumnsJson = schemaJson;
                    }
                    if (!string.IsNullOrWhiteSpace(partitionJson))
                    {
                        match.PartitionColumnsJson = partitionJson;
                    }
                    var prefixConflict = existingDatasets.Any(d => d.Id != match.Id && string.Equals(d.GcsPrefix, gcsPrefix, StringComparison.OrdinalIgnoreCase));
                    if (!prefixConflict)
                    {
                        match.GcsPrefix = gcsPrefix;
                    }
                    await CanonicalDatasets.UpdateAsync(match, autoSave: true);
                }
                else
                {
                    var prefixConflict = existingDatasets.Any(d => string.Equals(d.GcsPrefix, gcsPrefix, StringComparison.OrdinalIgnoreCase));
                    if (prefixConflict)
                    {
                        gcsPrefix = $"canonical/{code.ToLowerInvariant()}_{Guid.NewGuid():N}";
                    }
                    var newDataset = new CanonicalDataset
                    {
                        Code = code,
                        Name = name,
                        Description = description,
                        GcsPrefix = gcsPrefix,
                        SchemaColumnsJson = string.IsNullOrWhiteSpace(schemaJson) ? "[]" : schemaJson,
                        PartitionColumnsJson = string.IsNullOrWhiteSpace(partitionJson) ? "[]" : partitionJson,
                    };
                    await CanonicalDatasets.InsertAsync(newDataset, autoSave: true);
                    existingDatasets.Add(newDataset);
                }
            }
        }

        // Upsert Parsers
        if (input.Parsers != null && input.Parsers.Count > 0)
        {
            var existingParsers = await Parsers.GetListAsync();
            var allDatasets = await CanonicalDatasets.GetListAsync();
            foreach (var parserDto in input.Parsers)
            {
                var parserName = (parserDto.Name ?? "").Trim();
                if (string.IsNullOrEmpty(parserName)) continue;

                var pipelineJson = parserDto.PipelineJson;
                if ((string.IsNullOrWhiteSpace(pipelineJson) || pipelineJson == "{}") &&
                    parserDto.ExtraProperties.TryGetValue("pipeline", out var pipeEl) &&
                    pipeEl.ValueKind == JsonValueKind.Object)
                {
                    pipelineJson = pipeEl.GetRawText();
                }

                var fileFormat = string.IsNullOrWhiteSpace(parserDto.FileFormat) ? "csv" : parserDto.FileFormat.Trim();
                var outputGcsPrefix = (parserDto.OutputGcsPrefix ?? "").Trim().Trim('/');

                var matchingDataset = allDatasets.FirstOrDefault(d =>
                    !string.IsNullOrWhiteSpace(outputGcsPrefix) &&
                    string.Equals(d.GcsPrefix.Trim().Trim('/'), outputGcsPrefix, StringComparison.OrdinalIgnoreCase));

                if (matchingDataset == null)
                {
                    matchingDataset = allDatasets.FirstOrDefault(d =>
                    {
                        var normName = parserName.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        var normCode = d.Code.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        var normDsName = d.Name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                        return normName.Contains(normCode) || normCode.Contains(normName) ||
                               normName.Contains(normDsName) || normDsName.Contains(normName);
                    });
                }

                if (matchingDataset != null)
                {
                    outputGcsPrefix = matchingDataset.GcsPrefix.Trim().Trim('/');
                    pipelineJson = SynchronizeStudioDatasetId(pipelineJson, matchingDataset);
                }

                var match = existingParsers.FirstOrDefault(p => string.Equals(p.Name, parserName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    match.FileFormat = fileFormat;
                    if (!string.IsNullOrWhiteSpace(pipelineJson) && pipelineJson != "{}")
                    {
                        match.PipelineJson = pipelineJson;
                    }
                    if (!string.IsNullOrWhiteSpace(outputGcsPrefix))
                    {
                        match.OutputGcsPrefix = outputGcsPrefix;
                    }
                    match.IsActive = parserDto.IsActive;
                    await Parsers.UpdateAsync(match, autoSave: true);
                }
                else
                {
                    var newParser = new ParserConfiguration
                    {
                        Name = parserName,
                        FileFormat = fileFormat,
                        PipelineJson = string.IsNullOrWhiteSpace(pipelineJson) ? "{}" : pipelineJson,
                        OutputGcsPrefix = outputGcsPrefix,
                        IsActive = parserDto.IsActive,
                    };
                    if (parserDto.CreatedWithAi)
                    {
                        newParser.ExtraProperties["CreatedWithAi"] = true;
                    }
                    await Parsers.InsertAsync(newParser, autoSave: true);
                    existingParsers.Add(newParser);
                }
            }
        }

        // Create Package
        var packageName = !string.IsNullOrWhiteSpace(input.Package?.Name)
            ? input.Package.Name.Trim()
            : profileName;

        var package = new Package
        {
            Name = packageName,
            Description = input.Package?.Description ?? input.Profile.Description,
        };
        await _packages.InsertAsync(package, autoSave: true);

        // Create Profile
        var definitionFinalJson = ProfileJson.Write(definition);
        var isApproved = string.Equals(input.Profile.Status, "Approved", StringComparison.OrdinalIgnoreCase);
        var profile = new Profile
        {
            PackageId = package.Id,
            Name = profileName,
            Version = input.Profile.Version,
            Status = isApproved ? ProfileStatus.Approved : ProfileStatus.Draft,
            DefinitionJson = definitionFinalJson,
            ApprovedDefinitionJson = isApproved ? definitionFinalJson : null,
        };

        if (input.Profile.CreatedWithAi)
        {
            profile.ExtraProperties["CreatedWithAi"] = true;
        }

        await _profiles.InsertAsync(profile, autoSave: true);

        return Map(profile, package, false);
    }

    private static string NormalizePrefix(string? prefix)
    {
        return (prefix ?? "").Trim().Trim('/').ToLowerInvariant();
    }

    private static Guid? TryGetStudioDatasetId(string? pipelineJson)
    {
        if (string.IsNullOrWhiteSpace(pipelineJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(pipelineJson);
            if (document.RootElement.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            {
                foreach (var step in steps.EnumerateArray())
                {
                    if (step.TryGetProperty("id", out var id) && id.GetString() == "__studio" &&
                        step.TryGetProperty("args", out var args) && args.TryGetProperty("studio", out var studio) &&
                        studio.TryGetProperty("datasetId", out var value) && Guid.TryParse(value.GetString(), out var parsedId))
                    {
                        return parsedId;
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static string SynchronizeStudioDatasetId(string? pipelineJson, CanonicalDataset dataset)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pipelineJson) || pipelineJson == "{}")
            {
                return GenerateStarterPipelineJson(dataset);
            }

            var node = JsonNode.Parse(pipelineJson);
            if (node is not JsonObject root)
            {
                return GenerateStarterPipelineJson(dataset);
            }

            var steps = root["steps"] as JsonArray;
            if (steps == null)
            {
                steps = new JsonArray();
                root["steps"] = steps;
            }

            JsonObject? studioStep = null;
            foreach (var step in steps)
            {
                if (step is JsonObject stepObj && stepObj["id"]?.GetValue<string>() == "__studio")
                {
                    studioStep = stepObj;
                    break;
                }
            }

            if (studioStep == null)
            {
                studioStep = new JsonObject
                {
                    ["id"] = "__studio",
                    ["op"] = "profile",
                    ["enabled"] = false,
                    ["args"] = new JsonObject
                    {
                        ["studio"] = new JsonObject
                        {
                            ["datasetId"] = dataset.Id.ToString()
                        }
                    }
                };
                steps.Add(studioStep);
            }
            else
            {
                var args = studioStep["args"] as JsonObject;
                if (args == null)
                {
                    args = new JsonObject();
                    studioStep["args"] = args;
                }
                var studio = args["studio"] as JsonObject;
                if (studio == null)
                {
                    studio = new JsonObject();
                    args["studio"] = studio;
                }
                studio["datasetId"] = dataset.Id.ToString();
            }

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return GenerateStarterPipelineJson(dataset);
        }
    }

    private static string GenerateStarterPipelineJson(CanonicalDataset dataset)
    {
        var pipelineId = Guid.NewGuid().ToString();
        var safeCode = (dataset.Code ?? "dataset").Replace("-", "_").Replace(" ", "_");
        
        var columns = new JsonArray();
        var mappings = new JsonArray();
        try
        {
            if (!string.IsNullOrWhiteSpace(dataset.SchemaColumnsJson))
            {
                using var doc = JsonDocument.Parse(dataset.SchemaColumnsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var col in doc.RootElement.EnumerateArray())
                    {
                        if (col.TryGetProperty("name", out var nameProp))
                        {
                            var colName = nameProp.GetString() ?? "";
                            if (!string.IsNullOrEmpty(colName))
                            {
                                columns.Add(new JsonObject { ["name"] = colName, ["included"] = true });
                                var type = col.TryGetProperty("polarsType", out var pt) ? pt.GetString() ?? "string" : "string";
                                mappings.Add(new JsonObject
                                {
                                    ["sourceColumn"] = colName,
                                    ["target"] = colName,
                                    ["targetType"] = type.ToLowerInvariant().Contains("date") ? "date" : (type.ToLowerInvariant().Contains("decimal") ? "decimal" : "string"),
                                    ["sourceKind"] = "column"
                                });
                            }
                        }
                    }
                }
            }
        }
        catch { }

        var pipeline = new JsonObject
        {
            ["dslVersion"] = "2.0",
            ["pipeline"] = new JsonObject
            {
                ["id"] = pipelineId,
                ["revision"] = 1,
                ["name"] = $"{safeCode}_Parser"
            },
            ["inputs"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "main",
                    ["connector"] = "csv",
                    ["binding"] = "execution.input",
                    ["options"] = new JsonObject
                    {
                        ["hasHeader"] = true,
                        ["delimiter"] = ","
                    }
                }
            },
            ["steps"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "__studio",
                    ["op"] = "profile",
                    ["enabled"] = false,
                    ["args"] = new JsonObject
                    {
                        ["studio"] = new JsonObject
                        {
                            ["description"] = $"Parser for {dataset.Name}",
                            ["datasetId"] = dataset.Id.ToString(),
                            ["expectedFormat"] = "csv",
                            ["columns"] = columns,
                            ["mappings"] = mappings,
                            ["steps"] = new JsonArray()
                        }
                    }
                }
            },
            ["quality"] = new JsonObject
            {
                ["defaultErrorAction"] = "quarantine",
                ["quarantine"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["relativePath"] = "_quarantine"
                }
            },
            ["sink"] = new JsonObject
            {
                ["connector"] = "parquet",
                ["binding"] = "execution.output",
                ["writeMode"] = "overwrite"
            }
        };

        return pipeline.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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
            AiConfigurationId = profile.ExtraProperties.TryGetValue("AiConfigurationId", out var configurationId) && Guid.TryParse(configurationId?.ToString(), out var parsedId) ? parsedId : null,
            CreatedWithAi = MarkedByAi(profile.ExtraProperties),
        };
    }

    private static bool MarkedByAi(IDictionary<string, object?> values)
    {
        if (!values.TryGetValue("CreatedWithAi", out var value) || value is null) return false;
        return value switch
        {
            bool flag => flag,
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            _ => bool.TryParse(value.ToString(), out var parsed) && parsed
        };
    }
}

public class ReconciliationData : ITransientDependency
{
    private readonly IRepository<Dataset, Guid> _datasets;
    private readonly IRepository<DatasetRow, Guid> _rows;
    private readonly IRepository<RegressionBaseline, Guid> _baselines;
    private readonly CanonicalDataSource _canonical;

    public ReconciliationData(
        IRepository<Dataset, Guid> datasets,
        IRepository<DatasetRow, Guid> rows,
        IRepository<RegressionBaseline, Guid> baselines,
        CanonicalDataSource canonical)
    {
        _datasets = datasets;
        _rows = rows;
        _baselines = baselines;
        _canonical = canonical;
    }

    public async Task<Dictionary<string, List<Dictionary<string, string>>>> LoadAsync(
        IEnumerable<string> codes,
        Dictionary<string, Guid>? parserRunIds = null,
        Dictionary<string, List<Guid>>? multiParserRunIds = null,
        bool requireParserRuns = false)
    {
        var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var code in codes.Distinct(StringComparer.Ordinal))
        {
            IEnumerable<Guid>? runIds = null;
            if (multiParserRunIds != null && multiParserRunIds.TryGetValue(code, out var multiList) && multiList != null && multiList.Count > 0)
            {
                runIds = multiList;
            }
            else if (parserRunIds != null && parserRunIds.TryGetValue(code, out var runId))
            {
                runIds = new[] { runId };
            }

            var canonical = await _canonical.LoadMultipleAsync(code, runIds, requireParserRuns);
            if (canonical != null)
            {
                result[code] = canonical;
                continue;
            }
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

    public async Task<Dictionary<string, List<Dictionary<string, string>>>> LoadSchemasAsync(IEnumerable<string> codes)
    {
        var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
        foreach (var code in codes.Distinct(StringComparer.Ordinal))
        {
            var schema = await _canonical.GetSchemaAsync(code);
            if (schema != null) result[code] = [schema];
            else
            {
                var legacy = await LoadAsync([code]);
                result[code] = legacy[code];
            }
        }
        return result;
    }

    public async Task EnsureBaselineAsync(Guid packageId, string definition, Dictionary<string, List<Dictionary<string, string>>> rows)
    {
        if (await _baselines.AnyAsync(item => item.PackageId == packageId)) return;
        await _baselines.InsertAsync(new RegressionBaseline
        {
            PackageId = packageId,
            DatasetsJson = JsonSerializer.Serialize(rows),
            ProfileJson = definition,
        }, autoSave: true);
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
public class RunAppService : ApplicationService, IRunAppService
{
    private readonly IRepository<ReconciliationRunSummary, Guid> _summaries;
    private readonly IRepository<Profile, Guid> _profiles;
    private readonly IRepository<ReconciliationRun, Guid> _runs;
    private readonly IRepository<MatchGroup, Guid> _groups;
    private readonly IRepository<RunRecord, Guid> _records;
    private readonly ReconciliationData _data;

    public RunAppService(
        IRepository<ReconciliationRunSummary, Guid> summaries,
        IRepository<Profile, Guid> profiles,
        IRepository<ReconciliationRun, Guid> runs,
        IRepository<MatchGroup, Guid> groups,
        IRepository<RunRecord, Guid> records,
        ReconciliationData data)
    {
        _summaries = summaries;
        _profiles = profiles;
        _runs = runs;
        _groups = groups;
        _records = records;
        _data = data;
    }

    public async Task<List<RunDto>> GetListAsync(RunListInput input)
    {
        var query = await _runs.GetQueryableAsync();
        if (input.ProfileId.HasValue) query = query.Where(run => run.ProfileId == input.ProfileId.Value);
        var runs = await AsyncExecuter.ToListAsync(query.OrderByDescending(run => run.CreationTime));
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
            rows = await _data.LoadAsync(definition.Datasets, input.ParserRunIds, input.MultiParserRunIds, requireParserRuns: true);
            if (profile.ExtraProperties.TryGetValue("CreatedWithAi", out var aiOrigin) && bool.TryParse(aiOrigin?.ToString(), out var isAi) && isAi)
            {
                var assessment = AiConfigurationValidator.Assess(definition, rows, preview: false);
                if (!assessment.Passed) throw new ReconciliationException(string.Join(" ", assessment.Errors));
            }
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
        if (input.MultiParserRunIds != null && input.MultiParserRunIds.Count > 0)
        {
            run.ExtraProperties["MultiParserRunIds"] = input.MultiParserRunIds;
        }
        run.ExtraProperties["ParserRunIds"] = input.ParserRunIds;
        await _runs.InsertAsync(run, autoSave: true);
        await _data.EnsureBaselineAsync(profile.PackageId, profile.ApprovedDefinitionJson, rows);

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

        return MapDetail(run, groups, unresolved);
    }

    private static RunDto MapSummary(ReconciliationRun run)
    {
        return new RunDto
        {
            Id = run.Id,
            ProfileId = run.ProfileId,
            ProfileName = ProfileJson.Parse(run.DefinitionSnapshotJson).Name,
            CreatorId = run.CreatorId,
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
            ProfileName = ProfileJson.Parse(run.DefinitionSnapshotJson).Name,
            CreatorId = run.CreatorId,
            Datasets = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(run.DatasetSnapshotJson) ?? [],
            ProfileVersion = run.ProfileVersion,
            Status = run.Status.ToString(),
            CreationTime = run.CreationTime,
            TotalRecords = run.TotalRecords,
            MatchedRecords = run.MatchedRecords,
            UnresolvedRecords = run.UnresolvedRecords,
            AmbiguousRecords = run.AmbiguousRecords,
            MatchedGroups = run.MatchedGroups,
            Groups = groups.Select(group =>
            {
                var evidence = JsonSerializer.Deserialize<EvidenceDto>(group.EvidenceJson, ProfileJson.Storage) ?? new EvidenceDto();
                return new MatchGroupDto
                {
                    Rule = group.RuleId,
                    RuleType = evidence.Comparison == "partial_match" ? "partial" : "exact",
                    Key = JsonSerializer.Deserialize<List<string>>(group.KeyJson) ?? [],
                    Records = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(group.RecordsJson) ?? [],
                    Evidence = evidence,
                    Remarks = evidence.Remarks,
                    Score = evidence.Score,
                };
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
