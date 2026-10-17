using System.Threading;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;

namespace Grid.Datasets;

internal static class GoogleCloudClients
{
    public static Task<PublisherServiceApiClient> PublisherAdminAsync(CancellationToken cancellationToken = default) =>
        new PublisherServiceApiClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOrProduction }.BuildAsync(cancellationToken);

    public static Task<PublisherClient> PublisherAsync(TopicName topic, CancellationToken cancellationToken = default) =>
        new PublisherClientBuilder
        {
            TopicName = topic,
            EmulatorDetection = EmulatorDetection.EmulatorOrProduction
        }.BuildAsync(cancellationToken);
}
