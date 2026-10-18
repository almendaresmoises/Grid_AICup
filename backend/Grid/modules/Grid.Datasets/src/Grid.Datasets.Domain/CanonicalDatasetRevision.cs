using Volo.Abp.Domain.Entities;

namespace Grid.Datasets;

public class CanonicalDatasetRevision : Entity<Guid>
{
    public CanonicalDatasetRevision() { }
    public CanonicalDatasetRevision(Guid id) : base(id) { }

    public Guid CanonicalDatasetId { get; set; }
    public int RevisionNumber { get; set; }
    public string ContentHash { get; set; } = "";
    public string SchemaSnapshotJson { get; set; } = "[]";
    public Guid? ProvenanceAnalysisId { get; set; }
    public Guid? ProvenanceCommitId { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
