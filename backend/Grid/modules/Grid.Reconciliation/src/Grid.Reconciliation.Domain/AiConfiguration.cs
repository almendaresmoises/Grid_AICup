using Volo.Abp.Domain.Entities.Auditing;

namespace Grid.Reconciliation;

/// <summary>Durable configuration work after upload receipts have been accepted.</summary>
public class AiConfiguration : CreationAuditedAggregateRoot<Guid>
{
    public AiConfiguration() { }
    public AiConfiguration(Guid id) : base(id) { }
    public string Status { get; set; } = "WaitingForParsers";
    public string ManifestJson { get; set; } = "{}";
    public string ResultJson { get; set; } = "{}";
    public string? Error { get; set; }
    public Guid? ProfileId { get; set; }
    public Guid? RunId { get; set; }
    public string? ExecutedRevision { get; set; }
}
