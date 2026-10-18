using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Google;
using Google.Cloud.PubSub.V1;
using Google.Cloud.Storage.V1;
using Google.Protobuf;
using Grpc.Core;
using Grid.Datasets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;

namespace Grid.Parsers;

[Authorize]
public class ParserAppService : ApplicationService, IParserAppService
{
    private readonly IRepository<ParserConfiguration, Guid> _parsers;
    private readonly IRepository<ParserRun, Guid> _runs;

    public ParserAppService(
        IRepository<ParserConfiguration, Guid> parsers,
        IRepository<ParserRun, Guid> runs)
    {
        _parsers = parsers;
        _runs = runs;
    }

    public async Task<List<ParserConfigurationDto>> GetListAsync()
    {
        var parsers = await _parsers.GetListAsync();
        return parsers.OrderBy(parser => parser.Name, StringComparer.Ordinal).Select(Map).ToList();
    }

    public async Task<ParserConfigurationDto> GetAsync(Guid id)
    {
        return Map(await _parsers.GetAsync(id));
    }

    public async Task<ParserConfigurationDto> CreateAsync(CreateParserConfigurationDto input)
    {
        EnsureValid(input.Name, input.PipelineJson);
        using (DataFilter.Disable<ISoftDelete>())
        {
            var old = await _parsers.FirstOrDefaultAsync(p => p.Name == input.Name.Trim());
            if (old != null && old.IsDeleted)
            {
                await _runs.DeleteAsync(run => run.ParserConfigurationId == old.Id, autoSave: true);
                await _parsers.HardDeleteAsync(old, autoSave: true);
            }
        }
        var parser = new ParserConfiguration
        {
            Name = input.Name.Trim(),
            FileFormat = string.IsNullOrWhiteSpace(input.FileFormat) ? "csv" : input.FileFormat.Trim(),
            PipelineJson = input.PipelineJson,
            OutputGcsPrefix = input.OutputGcsPrefix.Trim().Trim('/'),
            IsActive = input.IsActive
        };
        if (input.CreatedWithAi)
        {
            parser.ExtraProperties["CreatedWithAi"] = true;
        }
        await _parsers.InsertAsync(parser, autoSave: true);
        return Map(parser);
    }

    public async Task<ParserConfigurationDto> UpdateAsync(Guid id, UpdateParserConfigurationDto input)
    {
        EnsureValid(input.Name, input.PipelineJson);
        var parser = await _parsers.GetAsync(id);
        parser.Name = input.Name.Trim();
        parser.FileFormat = string.IsNullOrWhiteSpace(input.FileFormat) ? parser.FileFormat : input.FileFormat.Trim();
        parser.PipelineJson = input.PipelineJson;
        parser.OutputGcsPrefix = input.OutputGcsPrefix.Trim().Trim('/');
        parser.IsActive = input.IsActive;
        await _parsers.UpdateAsync(parser, autoSave: true);
        return Map(parser);
    }

    public async Task DeleteAsync(Guid id)
    {
        await _runs.DeleteAsync(run => run.ParserConfigurationId == id, autoSave: true);
        await _parsers.DeleteAsync(id, autoSave: true);
    }

    private static void EnsureValid(string name, string pipelineJson)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new UserFriendlyException("Parser name is required.");
        var error = ParserPipelineDefinitionValidator.Validate(pipelineJson);
        if (error != null) throw new UserFriendlyException(error);
    }

    private static ParserConfigurationDto Map(ParserConfiguration parser) => new()
    {
        Id = parser.Id,
        Name = parser.Name,
        FileFormat = parser.FileFormat,
        PipelineJson = parser.PipelineJson,
        OutputGcsPrefix = parser.OutputGcsPrefix,
        IsActive = parser.IsActive,
        CreatedWithAi = MarkedByAi(parser.ExtraProperties)
    };

    internal static bool MarkedByAi(IDictionary<string, object?> values)
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

[RemoteService(false)]
public class ParserRunAppService : ApplicationService, IParserRunAppService
{
    private readonly IRepository<ParserConfiguration, Guid> _parsers;
    private readonly IRepository<ParserRun, Guid> _runs;
    private readonly IRepository<CanonicalDataset, Guid> _datasets;
    private readonly GoogleCloudOptions _google;

    public ParserRunAppService(
        IRepository<ParserConfiguration, Guid> parsers,
        IRepository<ParserRun, Guid> runs,
        IRepository<CanonicalDataset, Guid> datasets,
        IOptions<GoogleCloudOptions> google)
    {
        _parsers = parsers;
        _runs = runs;
        _datasets = datasets;
        _google = google.Value;
    }

    [Authorize]
    public async Task<List<ParserRunDto>> GetListAsync(Guid parserId)
    {
        var runs = await _runs.GetListAsync(run => run.ParserConfigurationId == parserId);
        return runs.OrderByDescending(run => run.CreationTime).Select(Map).ToList();
    }

    [Authorize]
    [RemoteService(false)]
    public async Task<ParserRunDto> StartAsync(Guid parserId, string fileName, byte[] content)
    {
        if (content.Length == 0) throw new UserFriendlyException("Choose a file to parse.");
        if (content.Length > 50 * 1024 * 1024) throw new UserFriendlyException("Parser uploads are limited to 50 MB.");
        if (string.IsNullOrWhiteSpace(_google.ProjectId) || string.IsNullOrWhiteSpace(_google.Bucket))
            throw new UserFriendlyException("Set GoogleCloud:ProjectId and GoogleCloud:Bucket before starting a parser run.");

        var parser = await _parsers.GetAsync(parserId);
        if (!parser.IsActive) throw new UserFriendlyException("This parser is inactive.");
        var error = ParserPipelineDefinitionValidator.Validate(parser.PipelineJson);
        if (error != null) throw new UserFriendlyException(error);
        var dataset = await ResolveDatasetAsync(parser);
        if (!DatasetContractValidator.TryReadContract(dataset.SchemaColumnsJson, out var fields))
            throw new UserFriendlyException("The selected dataset contract is invalid.");
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName)) throw new UserFriendlyException("The uploaded file needs a name.");

        var run = new ParserRun(GuidGenerator.Create())
        {
            ParserConfigurationId = parser.Id,
            Status = "Queued",
            FileName = safeName,
            PipelineHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(parser.PipelineJson))).ToLowerInvariant()
        };
        run.ExtraProperties["DatasetId"] = dataset.Id.ToString();
        run.ExtraProperties["DatasetCode"] = dataset.Code;
        run.ExtraProperties["SchemaColumnsJson"] = dataset.SchemaColumnsJson;
        run.ExtraProperties["PipelineJson"] = parser.PipelineJson;
        var prefix = dataset.GcsPrefix;
        run.InputObject = $"gs://{_google.Bucket}/{prefix}/inbox/{run.Id}/{safeName}";
        run.OutputObject = $"gs://{_google.Bucket}/{prefix}/out/{run.Id}/data.parquet";

        var storage = await GoogleCloudClients.StorageAsync();
        using var stream = new MemoryStream(content);
        try
        {
            await storage.UploadObjectAsync(_google.Bucket, ObjectName(run.InputObject), "application/octet-stream", stream);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGE_EMULATOR_HOST")))
        {
            try
            {
                await storage.CreateBucketAsync(_google.ProjectId, _google.Bucket);
            }
            catch (GoogleApiException)
            {
                // Another thread may have just created it
            }
            stream.Position = 0;
            await storage.UploadObjectAsync(_google.Bucket, ObjectName(run.InputObject), "application/octet-stream", stream);
        }

        await _runs.InsertAsync(run, autoSave: true);
        try
        {
            await PublishAsync(parser, run, fields);
        }
        catch (Exception)
        {
            run.Status = "Failed";
            run.Error = "The parser job could not be queued. Upload the file again.";
            await _runs.UpdateAsync(run, autoSave: true);
        }
        return Map(run);
    }

    [AllowAnonymous]
    [RemoteService(false)]
    public async Task CompleteAsync(Guid runId, CompleteParserRunDto input, string? workerSecret)
    {
        if (string.IsNullOrEmpty(_google.WorkerSecret) || !FixedEquals(_google.WorkerSecret, workerSecret))
            throw new UserFriendlyException("Worker secret was rejected.");
        if (input.Status is not ("Succeeded" or "Failed"))
            throw new UserFriendlyException("Run status must be Succeeded or Failed.");

        var run = await _runs.GetAsync(runId);
        if (run.Status is "Succeeded" or "Failed")
        {
            if (run.Status == input.Status) return;
            throw new UserFriendlyException("This parser run has already completed with a different status.");
        }
        run.Status = input.Status;
        run.Error = string.IsNullOrWhiteSpace(input.Error) ? null : (input.Error.Length > 3900 ? input.Error[..3900] + "..." : input.Error);
        run.ReportJson = input.ReportJson;
        await _runs.UpdateAsync(run, autoSave: true);
    }

    private async Task PublishAsync(ParserConfiguration parser, ParserRun run, List<DatasetContractField> fields)
    {
        var topic = TopicName.FromProjectTopic(_google.ProjectId, string.IsNullOrWhiteSpace(_google.ParserTopicId) ? "grid-parser-runs" : _google.ParserTopicId);
        var admin = await GoogleCloudClients.PublisherAdminAsync();
        try
        {
            await admin.CreateTopicAsync(topic);
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.AlreadyExists or StatusCode.PermissionDenied)
        {
        }

        var publisher = await GoogleCloudClients.PublisherAsync(topic);
        var payload = JsonSerializer.Serialize(new
        {
            run_id = run.Id,
            parser_id = parser.Id,
            pipeline = JsonSerializer.Deserialize<JsonElement>(parser.PipelineJson),
            pipeline_hash = run.PipelineHash,
            input_uri = run.InputObject,
            output_uri = run.OutputObject,
            file_name = run.FileName,
            output_contract = fields.Select(field => new { name = field.Name, type = field.Type, required = field.IsRequired })
        });
        await publisher.PublishAsync(ByteString.CopyFromUtf8(payload));
    }

    private static string ObjectName(string uri) => uri[(uri.IndexOf('/', 5) + 1)..];

    private async Task<CanonicalDataset> ResolveDatasetAsync(ParserConfiguration parser)
    {
        var studioId = TryGetStudioDatasetId(parser.PipelineJson);
        if (studioId.HasValue)
        {
            var ds = await _datasets.FindAsync(studioId.Value);
            if (ds != null) return ds;
        }

        var allDatasets = await _datasets.GetListAsync();
        if (!string.IsNullOrWhiteSpace(parser.OutputGcsPrefix))
        {
            var normalized = parser.OutputGcsPrefix.Trim().Trim('/').ToLowerInvariant();
            var ds = allDatasets.FirstOrDefault(d => string.Equals(d.GcsPrefix.Trim().Trim('/'), normalized, StringComparison.OrdinalIgnoreCase));
            if (ds != null) return ds;
        }

        var cleanParserName = parser.Name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
        var fallback = allDatasets.FirstOrDefault(d =>
        {
            var cleanCode = d.Code.Replace(" ", "").Replace("_", "").ToLowerInvariant();
            var cleanDsName = d.Name.Replace(" ", "").Replace("_", "").ToLowerInvariant();
            return (!string.IsNullOrEmpty(cleanCode) && (cleanParserName.Contains(cleanCode) || cleanCode.Contains(cleanParserName))) ||
                   (!string.IsNullOrEmpty(cleanDsName) && (cleanParserName.Contains(cleanDsName) || cleanDsName.Contains(cleanParserName)));
        });
        if (fallback != null) return fallback;

        throw new UserFriendlyException("Select a canonical dataset in the parser studio and publish the parser before uploading files.");
    }

    private static Guid? TryGetStudioDatasetId(string pipelineJson)
    {
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

    private static bool FixedEquals(string expected, string? actual)
    {
        var left = System.Text.Encoding.UTF8.GetBytes(expected);
        var right = System.Text.Encoding.UTF8.GetBytes(actual ?? "");
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static ParserRunDto Map(ParserRun run) => new()
    {
        Id = run.Id,
        ParserConfigurationId = run.ParserConfigurationId,
        Status = run.Status,
        FileName = run.FileName,
        InputObject = run.InputObject,
        OutputObject = run.OutputObject,
        Error = run.Error,
        ReportJson = run.ReportJson,
        CreationTime = run.CreationTime
    };
}
