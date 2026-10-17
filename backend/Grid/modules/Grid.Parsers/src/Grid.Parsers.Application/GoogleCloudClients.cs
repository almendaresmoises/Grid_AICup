using System.Threading;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Google.Cloud.Storage.V1;

namespace Grid.Parsers;

internal static class GoogleCloudClients
{
    public static Task<StorageClient> StorageAsync(CancellationToken cancellationToken = default)
    {
        var builder = new StorageClientBuilder();
        var host = Environment.GetEnvironmentVariable("STORAGE_EMULATOR_HOST");
        if (!string.IsNullOrWhiteSpace(host))
        {
            builder.BaseUri = host.TrimEnd('/') + (host.TrimEnd('/').EndsWith("/storage/v1", StringComparison.OrdinalIgnoreCase) ? "/" : "/storage/v1/");
            builder.UnauthenticatedAccess = true;
        }
        else
        {
            builder.EmulatorDetection = EmulatorDetection.EmulatorOrProduction;
        }
        return builder.BuildAsync(cancellationToken);
    }

    public static Task<PublisherServiceApiClient> PublisherAdminAsync(CancellationToken cancellationToken = default) =>
        new PublisherServiceApiClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOrProduction }.BuildAsync(cancellationToken);

    public static Task<PublisherClient> PublisherAsync(TopicName topic, CancellationToken cancellationToken = default) =>
        new PublisherClientBuilder
        {
            TopicName = topic,
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction
        }.BuildAsync(cancellationToken);
}
