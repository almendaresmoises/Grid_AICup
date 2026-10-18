using System.Security.Cryptography;
using System.Text.Json;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;

namespace Grid.Datasets;

[Authorize]
public class CanonicalDatasetAppService : ApplicationService, ICanonicalDatasetAppService
{
    private readonly IRepository<CanonicalDataset, Guid> _datasets;
    private readonly GoogleCloudOptions _google;

    public CanonicalDatasetAppService(IRepository<CanonicalDataset, Guid> datasets, IOptions<GoogleCloudOptions> google)
    {
        _datasets = datasets;
        _google = google.Value;
    }

    public async Task<List<CanonicalDatasetDto>> GetListAsync()
    {
        var datasets = await _datasets.GetListAsync();
        return datasets.OrderBy(dataset => dataset.Code, StringComparer.Ordinal).Select(Map).ToList();
    }

    public async Task<CanonicalDatasetDto> GetAsync(Guid id)
    {
        return Map(await _datasets.GetAsync(id));
    }

    public async Task<CanonicalDatasetDto> CreateAsync(CreateCanonicalDatasetDto input)
    {
        var dataset = new CanonicalDataset();
        Apply(dataset, input.Code, input.Name, input.Description, input.GcsPrefix, input.SchemaColumnsJson, input.PartitionColumnsJson);
        if (input.CreatedWithAi)
        {
            dataset.ExtraProperties["CreatedWithAi"] = true;
        }
        await CleanUpOrEnsureUniqueAsync(dataset, null);
        await _datasets.InsertAsync(dataset, autoSave: true);
        await PublishAsync(dataset, "dataset.upserted");
        return Map(dataset);
    }

    public async Task<CanonicalDatasetDto> UpdateAsync(Guid id, UpdateCanonicalDatasetDto input)
    {
        var dataset = await _datasets.GetAsync(id);
        Apply(dataset, input.Code, input.Name, input.Description, input.GcsPrefix, input.SchemaColumnsJson, input.PartitionColumnsJson);
        await CleanUpOrEnsureUniqueAsync(dataset, id);
        await _datasets.UpdateAsync(dataset, autoSave: true);
        await PublishAsync(dataset, "dataset.upserted");
        return Map(dataset);
    }

    public async Task DeleteAsync(Guid id)
    {
        var dataset = await _datasets.GetAsync(id);
        await PublishAsync(dataset, "dataset.deleted");
        await _datasets.HardDeleteAsync(dataset, autoSave: true);
    }

    [RemoteService(false)]
    public Task<DatasetOutputLocationDto> GetOutputLocationAsync(Guid id, string? workerSecret, bool authenticated)
    {
        return OutputAsync(_datasets.GetAsync(id), workerSecret, authenticated);
    }

    [RemoteService(false)]
    public async Task<DatasetOutputLocationDto> GetOutputLocationByCodeAsync(string code, string? workerSecret, bool authenticated)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var dataset = await _datasets.FirstOrDefaultAsync(item => item.Code == normalized)
            ?? throw new UserFriendlyException($"Dataset '{normalized}' was not found.");
        EnsureWorker(workerSecret, authenticated);
        return ToLocation(dataset);
    }

    private async Task<DatasetOutputLocationDto> OutputAsync(Task<CanonicalDataset> datasetTask, string? workerSecret, bool authenticated)
    {
        EnsureWorker(workerSecret, authenticated);
        return ToLocation(await datasetTask);
    }

    private void Apply(CanonicalDataset dataset, string code, string name, string description, string gcsPrefix, string schemaJson, string partitionJson)
    {
        var error = DatasetContractValidator.Validate(code, name, gcsPrefix, schemaJson, partitionJson);
        if (error != null) throw new UserFriendlyException(error);
        dataset.Code = code.Trim().ToUpperInvariant();
        dataset.Name = name.Trim();
        dataset.Description = description.Trim();
        dataset.GcsPrefix = gcsPrefix.Trim().ToLowerInvariant();
        dataset.SchemaColumnsJson = schemaJson;
        dataset.PartitionColumnsJson = string.IsNullOrWhiteSpace(partitionJson) ? "[]" : partitionJson;
    }

    private async Task CleanUpOrEnsureUniqueAsync(CanonicalDataset dataset, Guid? existingId)
    {
        using (DataFilter.Disable<ISoftDelete>())
        {
            var datasets = await _datasets.GetListAsync();
            var codeMatch = datasets.FirstOrDefault(item => item.Id != existingId && string.Equals(item.Code, dataset.Code, StringComparison.OrdinalIgnoreCase));
            if (codeMatch != null)
            {
                if (codeMatch.IsDeleted)
                {
                    await _datasets.HardDeleteAsync(codeMatch, autoSave: true);
                }
                else
                {
                    throw new UserFriendlyException($"Dataset code '{dataset.Code}' is already in use.");
                }
            }

            var prefixMatch = datasets.FirstOrDefault(item => item.Id != existingId && string.Equals(item.GcsPrefix, dataset.GcsPrefix, StringComparison.OrdinalIgnoreCase));
            if (prefixMatch != null && prefixMatch.Id != codeMatch?.Id)
            {
                if (prefixMatch.IsDeleted)
                {
                    await _datasets.HardDeleteAsync(prefixMatch, autoSave: true);
                }
                else
                {
                    throw new UserFriendlyException($"Dataset directory '{dataset.GcsPrefix}' is already in use.");
                }
            }
        }
    }

    private void EnsureWorker(string? workerSecret, bool authenticated)
    {
        if (authenticated) return;
        var expected = _google.WorkerSecret;
        if (string.IsNullOrEmpty(expected)) throw new UserFriendlyException("Set GoogleCloud:WorkerSecret before the worker can read dataset locations.");
        var left = System.Text.Encoding.UTF8.GetBytes(expected);
        var right = System.Text.Encoding.UTF8.GetBytes(workerSecret ?? "");
        if (left.Length != right.Length || !CryptographicOperations.FixedTimeEquals(left, right))
            throw new UserFriendlyException("Worker secret was rejected.");
    }

    private async Task PublishAsync(CanonicalDataset dataset, string eventName)
    {
        if (string.IsNullOrWhiteSpace(_google.ProjectId)) return;
        var topicName = string.IsNullOrWhiteSpace(_google.DatasetTopicId) ? "grid-datasets" : _google.DatasetTopicId;
        var topic = TopicName.FromProjectTopic(_google.ProjectId, topicName);
        var admin = await GoogleCloudClients.PublisherAdminAsync();
        try
        {
            await admin.CreateTopicAsync(topic);
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.AlreadyExists or StatusCode.PermissionDenied)
        {
        }

        DatasetContractValidator.TryReadContract(dataset.SchemaColumnsJson, out var fields);
        DatasetContractValidator.TryReadStringArray(dataset.PartitionColumnsJson, out var partitions);
        var payload = JsonSerializer.Serialize(new
        {
            @event = eventName,
            dataset_id = dataset.Id,
            code = dataset.Code,
            name = dataset.Name,
            output_uri = OutputUri(dataset.GcsPrefix),
            gcs_prefix = dataset.GcsPrefix,
            schema_columns = fields.Select(field => field.Name).ToArray(),
            partition_columns = partitions,
            fields = fields.Select(field => new { name = field.Name, type = field.Type, nullable = !field.IsRequired, semantic_role = field.SemanticRole })
        });
        var publisher = await GoogleCloudClients.PublisherAsync(topic);
        await publisher.PublishAsync(ByteString.CopyFromUtf8(payload));
    }

    private CanonicalDatasetDto Map(CanonicalDataset dataset) => new()
    {
        Id = dataset.Id,
        Code = dataset.Code,
        Name = dataset.Name,
        Description = dataset.Description,
        GcsPrefix = dataset.GcsPrefix,
        OutputUri = string.IsNullOrWhiteSpace(_google.Bucket) ? null : OutputUri(dataset.GcsPrefix),
        SchemaColumnsJson = dataset.SchemaColumnsJson,
        PartitionColumnsJson = dataset.PartitionColumnsJson,
        CreatedWithAi = MarkedByAi(dataset.ExtraProperties)
    };

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

    private DatasetOutputLocationDto ToLocation(CanonicalDataset dataset)
    {
        if (string.IsNullOrWhiteSpace(_google.Bucket))
            throw new UserFriendlyException("Set GoogleCloud:Bucket before resolving a dataset output location.");
        DatasetContractValidator.TryReadContract(dataset.SchemaColumnsJson, out var fields);
        DatasetContractValidator.TryReadStringArray(dataset.PartitionColumnsJson, out var partitions);
        var schemaColumns = fields.Select(field => field.Name).ToList();
        return new DatasetOutputLocationDto
        {
            Id = dataset.Id,
            DatasetCode = dataset.Code,
            DatasetName = dataset.Name,
            ProviderType = "GoogleCloudStorage",
            Bucket = _google.Bucket,
            GcsPrefix = dataset.GcsPrefix,
            ResolvedOutputUri = OutputUri(dataset.GcsPrefix),
            PartitionColumns = partitions,
            SchemaColumns = schemaColumns,
            OutputColumns = schemaColumns.Concat(partitions).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Fields = fields.Select(field => new DatasetFieldDto
            {
                Name = field.Name,
                Type = field.Type,
                Nullable = !field.IsRequired,
                SemanticRole = field.SemanticRole
            }).ToList()
        };
    }

    private string OutputUri(string prefix) => $"gs://{_google.Bucket}/{prefix}";
}
