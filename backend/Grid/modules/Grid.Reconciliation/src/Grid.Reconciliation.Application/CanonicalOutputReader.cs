using System.IO;
using System.Text;
using Google.Api.Gax;
using Google.Cloud.Storage.V1;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Grid.Reconciliation;

public interface ICanonicalOutputReader
{
    Task<List<Dictionary<string, string>>> ReadAsync(string uri);
}

public class CanonicalOutputReader : ICanonicalOutputReader, ITransientDependency
{
    public async Task<List<Dictionary<string, string>>> ReadAsync(string uri)
    {
        if (!uri.StartsWith("gs://", StringComparison.Ordinal)) throw new UserFriendlyException("The parser output is not in Cloud Storage.");
        var location = uri[5..].Split('/', 2);
        if (location.Length != 2) throw new UserFriendlyException("The parser output location is invalid.");
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
        var storage = await builder.BuildAsync();
        using var stream = new MemoryStream();
        try
        {
            await storage.DownloadObjectAsync(location[0], location[1], stream);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new UserFriendlyException("Canonical parser output is missing. Reprocess the source file.");
        }
        return ReconciliationEngine.ReadCsv(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
