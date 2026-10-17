namespace Grid.Datasets;

public class GoogleCloudOptions
{
    public string ProjectId { get; set; } = "";

    public string Bucket { get; set; } = "";

    public string DatasetTopicId { get; set; } = "grid-datasets";

    public string WorkerSecret { get; set; } = "";
}
