using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Grid.Reconciliation;

public interface IGeminiClient
{
    Task<JsonElement> GenerateJsonAsync(string prompt);
    Task<bool> TestConnectionAsync();
}

public class GeminiClient : IGeminiClient, ITransientDependency
{
    private readonly IConfiguration _configuration;

    public GeminiClient(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<JsonElement> GenerateJsonAsync(string prompt)
    {
        var key = FirstNonEmpty(_configuration["Gemini:ApiKey"], Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new UserFriendlyException("Assistant unavailable. Set GEMINI_API_KEY to enable AI advisor. Deterministic validation still works.");
        }

        var model = FirstNonEmpty(Environment.GetEnvironmentVariable("GEMINI_MODEL"), _configuration["Gemini:Model"], "gemini-3.8-flash");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
        request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
        request.Content = JsonContent.Create(new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = prompt } } },
            },
            generationConfig = new { responseMimeType = "application/json", temperature = 0.2 },
        });

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request);
        }
        catch (Exception ex)
        {
            throw new UserFriendlyException("The assistant could not be reached: " + ex.Message);
        }

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var detail = "";
            try
            {
                using var errorDoc = JsonDocument.Parse(body);
                if (errorDoc.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message))
                {
                    detail = message.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
                detail = "";
            }

            detail = detail.Replace(key, "[REDACTED]", StringComparison.Ordinal);
            if (detail.Length > 500) detail = detail[..500];

            var hint = (int)response.StatusCode switch
            {
                404 => " Verify GEMINI_MODEL. Use gemini-3.8-flash or another model enabled for your key.",
                401 or 403 => " Verify that your API key is active and permitted to call Gemini.",
                429 => " API rate limit or quota exceeded.",
                _ => "",
            };

            throw new UserFriendlyException($"Assistant returned status {(int)response.StatusCode}.{hint} {detail}".Trim());
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            throw new UserFriendlyException("The assistant returned no candidate responses.");
        }

        var text = candidates[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";

        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine >= 0 && lastFence > firstLine)
            {
                trimmed = trimmed[(firstLine + 1)..lastFence].Trim();
            }
        }

        return JsonDocument.Parse(trimmed).RootElement.Clone();
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await GenerateJsonAsync("Return exactly this JSON object: {\"connected\": true}");
            return response.TryGetProperty("connected", out var connected) && connected.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var val in values)
        {
            if (!string.IsNullOrWhiteSpace(val)) return val;
        }
        return "";
    }
}
