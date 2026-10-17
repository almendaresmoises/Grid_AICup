using System.Threading;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grid.Parsers;

public class LocalBucketInitializer : IHostedService
{
    private readonly GoogleCloudOptions _google;
    private readonly ILogger<LocalBucketInitializer> _logger;

    public LocalBucketInitializer(IOptions<GoogleCloudOptions> google, ILogger<LocalBucketInitializer> logger)
    {
        _google = google.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGE_EMULATOR_HOST")))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_google.ProjectId) || string.IsNullOrWhiteSpace(_google.Bucket))
        {
            return;
        }

        try
        {
            var storage = await GoogleCloudClients.StorageAsync(cancellationToken);
            try
            {
                await storage.GetBucketAsync(_google.Bucket, cancellationToken: cancellationToken);
            }
            catch (GoogleApiException exception) when (exception.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await storage.CreateBucketAsync(_google.ProjectId, _google.Bucket, cancellationToken: cancellationToken);
                _logger.LogInformation("Created local Cloud Storage bucket {Bucket}.", _google.Bucket);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Local Cloud Storage is configured, but the bucket was not created. Start docker compose and restart the API.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
