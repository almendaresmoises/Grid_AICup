using Volo.Abp.Domain.Entities;

namespace Grid.Parsers;

public class ParserConfigurationRevision : Entity<Guid>
{
    public ParserConfigurationRevision() { }
    public ParserConfigurationRevision(Guid id) : base(id) { }

    public Guid ParserConfigurationId { get; set; }
    public int RevisionNumber { get; set; }
    public string ContentHash { get; set; } = "";
    public string SnapshotJson { get; set; } = "{}";
    public Guid? ProvenanceAnalysisId { get; set; }
    public Guid? ProvenanceCommitId { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
