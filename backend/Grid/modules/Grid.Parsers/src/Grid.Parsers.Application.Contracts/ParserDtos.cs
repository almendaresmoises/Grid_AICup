namespace Grid.Parsers;

public class ParserConfigurationDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string FileFormat { get; set; } = "csv";

    public string PipelineJson { get; set; } = "{}";

    public string OutputGcsPrefix { get; set; } = "";

    public bool IsActive { get; set; }

    public bool CreatedWithAi { get; set; }
}

public class CreateParserConfigurationDto
{
    public string Name { get; set; } = "";

    public string FileFormat { get; set; } = "csv";

    public string PipelineJson { get; set; } = "{}";

    public string OutputGcsPrefix { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public bool CreatedWithAi { get; set; }
}

public class UpdateParserConfigurationDto
{
    public string Name { get; set; } = "";

    public string FileFormat { get; set; } = "csv";

    public string PipelineJson { get; set; } = "{}";

    public string OutputGcsPrefix { get; set; } = "";

    public bool IsActive { get; set; } = true;
}

public class ParserRunDto
{
    public Guid Id { get; set; }

    public Guid ParserConfigurationId { get; set; }

    public string Status { get; set; } = "";

    public string FileName { get; set; } = "";

    public string InputObject { get; set; } = "";

    public string OutputObject { get; set; } = "";

    public string? Error { get; set; }

    public string? ReportJson { get; set; }

    public DateTime CreationTime { get; set; }
}

public class CompleteParserRunDto
{
    public string Status { get; set; } = "";

    public string? Error { get; set; }

    public string? ReportJson { get; set; }
}
