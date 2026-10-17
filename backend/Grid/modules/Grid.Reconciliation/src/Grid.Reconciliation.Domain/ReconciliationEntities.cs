using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;

namespace Grid.Reconciliation;

public class Dataset : CreationAuditedAggregateRoot<Guid>
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string ColumnsJson { get; set; } = "[]";

    public int RowCount { get; set; }
}

public class DatasetRow : Entity<Guid>
{
    public Guid DatasetId { get; set; }

    public int RowNumber { get; set; }

    public string ValuesJson { get; set; } = "{}";
}

public class Package : CreationAuditedAggregateRoot<Guid>
{
    public Package() { }
    public Package(Guid id) : base(id) { }

    public string Name { get; set; } = "";

    public string? Description { get; set; }
}

public class Profile : CreationAuditedAggregateRoot<Guid>
{
    public Profile() { }
    public Profile(Guid id) : base(id) { }

    public Guid PackageId { get; set; }

    public string Name { get; set; } = "";

    public int Version { get; set; }

    public ProfileStatus Status { get; set; } = ProfileStatus.Draft;

    public string DefinitionJson { get; set; } = "{}";

    public string? ApprovedDefinitionJson { get; set; }
}

public class RegressionBaseline : Entity<Guid>
{
    public RegressionBaseline() { }
    public RegressionBaseline(Guid id) : base(id) { }

    public Guid PackageId { get; set; }

    public string DatasetsJson { get; set; } = "{}";

    public string ProfileJson { get; set; } = "{}";
}

public class ReconciliationRun : CreationAuditedAggregateRoot<Guid>
{
    public ReconciliationRun() { }
    public ReconciliationRun(Guid id) : base(id) { }

    public Guid ProfileId { get; set; }

    public Guid PackageId { get; set; }

    public int ProfileVersion { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Succeeded;

    public int TotalRecords { get; set; }

    public int MatchedRecords { get; set; }

    public int UnresolvedRecords { get; set; }

    public int AmbiguousRecords { get; set; }

    public int MatchedGroups { get; set; }

    public string DefinitionSnapshotJson { get; set; } = "{}";

    public string DatasetSnapshotJson { get; set; } = "{}";
}

public class MatchGroup : Entity<Guid>
{
    public Guid RunId { get; set; }

    public int Sequence { get; set; }

    public string RuleId { get; set; } = "";

    public string KeyJson { get; set; } = "[]";

    public string RecordsJson { get; set; } = "{}";

    public string EvidenceJson { get; set; } = "{}";
}

public class RunRecord : Entity<Guid>
{
    public Guid RunId { get; set; }

    public string DatasetCode { get; set; } = "";

    public int CsvRow { get; set; }

    public string Reason { get; set; } = "";

    public string RecordJson { get; set; } = "{}";
}
