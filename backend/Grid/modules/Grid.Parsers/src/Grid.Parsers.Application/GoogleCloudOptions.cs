namespace Grid.Parsers;

public class GoogleCloudOptions
{
    public string ProjectId { get; set; } = "";

    public string Bucket { get; set; } = "";

    public string ParserTopicId { get; set; } = "grid-parser-runs";

    public string WorkerSecret { get; set; } = "";
}
