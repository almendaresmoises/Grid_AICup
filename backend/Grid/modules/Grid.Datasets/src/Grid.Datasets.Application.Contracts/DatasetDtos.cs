namespace Grid.Datasets;

public class CanonicalDatasetDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string GcsPrefix { get; set; } = "";

    public string? OutputUri { get; set; }

    public string SchemaColumnsJson { get; set; } = "[]";

    public string PartitionColumnsJson { get; set; } = "[]";

    public bool CreatedWithAi { get; set; }
}

public class CreateCanonicalDatasetDto
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string GcsPrefix { get; set; } = "";

    public string SchemaColumnsJson { get; set; } = "[]";

    public string PartitionColumnsJson { get; set; } = "[]";

    public bool CreatedWithAi { get; set; }
}

public class UpdateCanonicalDatasetDto
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string GcsPrefix { get; set; } = "";

    public string SchemaColumnsJson { get; set; } = "[]";

    public string PartitionColumnsJson { get; set; } = "[]";

    public bool CreatedWithAi { get; set; }
}

public class DatasetFieldDto
{
    public string Name { get; set; } = "";

    public string Type { get; set; } = "string";

    public bool Nullable { get; set; } = true;

    public string SemanticRole { get; set; } = "none";
}

public class DatasetOutputLocationDto
{
    public Guid Id { get; set; }

    public string DatasetCode { get; set; } = "";

    public string DatasetName { get; set; } = "";

    public string ProviderType { get; set; } = "GoogleCloudStorage";

    public string Bucket { get; set; } = "";

    public string GcsPrefix { get; set; } = "";

    public string ResolvedOutputUri { get; set; } = "";

    public List<string> PartitionColumns { get; set; } = [];

    public List<string> SchemaColumns { get; set; } = [];

    public List<string> OutputColumns { get; set; } = [];

    public List<DatasetFieldDto> Fields { get; set; } = [];
}
