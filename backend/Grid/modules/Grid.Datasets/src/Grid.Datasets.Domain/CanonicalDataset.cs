using Volo.Abp.Domain.Entities.Auditing;

namespace Grid.Datasets;

public class CanonicalDataset : FullAuditedAggregateRoot<Guid>
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string GcsPrefix { get; set; } = "";

    public string SchemaColumnsJson { get; set; } = "[]";

    public string PartitionColumnsJson { get; set; } = "[]";
}
