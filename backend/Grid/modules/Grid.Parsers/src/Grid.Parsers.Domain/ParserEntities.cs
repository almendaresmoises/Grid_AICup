using Volo.Abp.Domain.Entities.Auditing;

namespace Grid.Parsers;

public class ParserConfiguration : FullAuditedAggregateRoot<Guid>
{
    public string Name { get; set; } = "";

    public string FileFormat { get; set; } = "csv";

    public string PipelineJson { get; set; } = "{}";

    public string OutputGcsPrefix { get; set; } = "";

    public bool IsActive { get; set; } = true;
}

public class ParserRun : CreationAuditedAggregateRoot<Guid>
{
    protected ParserRun()
    {
    }

    public ParserRun(Guid id) : base(id)
    {
    }

    public Guid ParserConfigurationId { get; set; }

    public string Status { get; set; } = "Queued";

    public string FileName { get; set; } = "";

    public string InputObject { get; set; } = "";

    public string OutputObject { get; set; } = "";

    public string PipelineHash { get; set; } = "";

    public string? Error { get; set; }

    public string? ReportJson { get; set; }
}
