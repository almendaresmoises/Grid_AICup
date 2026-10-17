using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grid.Reconciliation;

public enum ProfileStatus
{
    Draft = 0,
    Approved = 1,
}

public enum RunStatus
{
    Succeeded = 0,
    Failed = 1,
}

public sealed class ProfileDefinition
{
    public string Name { get; set; } = "";

    public int Version { get; set; }

    public List<string> Datasets { get; set; } = [];

    public List<RuleDefinition> Rules { get; set; } = [];
}

public sealed class RuleDefinition
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "";

    public List<string> Datasets { get; set; } = [];

    public List<KeyDefinition> Keys { get; set; } = [];

    public string? Target { get; set; }

    public string? Source { get; set; }

    [JsonPropertyName("target_amount")]
    public string? TargetAmount { get; set; }

    [JsonPropertyName("source_amount")]
    public string? SourceAmount { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Tolerance { get; set; }

    [JsonPropertyName("min_score")]
    public decimal? MinScore { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }
}

public sealed class KeyDefinition
{
    public string Type { get; set; } = "";

    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("strip_prefixes")]
    public Dictionary<string, List<string>>? StripPrefixes { get; set; }

    [JsonPropertyName("match_mode")]
    public string? MatchMode { get; set; }

    [JsonPropertyName("algorithm")]
    public string? Algorithm { get; set; }

    [JsonPropertyName("min_score")]
    public decimal? MinScore { get; set; }

    [JsonPropertyName("tolerance")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Tolerance { get; set; }

    [JsonPropertyName("expected_mismatch")]
    public bool? ExpectedMismatch { get; set; }
}

public sealed class MatchGroupResult
{
    public string Rule { get; set; } = "";

    public string RuleType { get; set; } = "exact";

    public List<string> Key { get; set; } = [];

    public Dictionary<string, List<int>> Records { get; set; } = new(StringComparer.Ordinal);

    public EvidenceResult Evidence { get; set; } = new();

    public string? Remarks { get; set; }

    public decimal? Score { get; set; }
}

public sealed class EvidenceResult
{
    public string Comparison { get; set; } = "";

    public string? Target { get; set; }

    public string? SourceSum { get; set; }

    public string? Variance { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [JsonPropertyName("score")]
    public decimal? Score { get; set; }

    [JsonPropertyName("divergent_key")]
    public string? DivergentKey { get; set; }
}

public sealed class UnresolvedResult
{
    public string Dataset { get; set; } = "";

    public int CsvRow { get; set; }

    public string Reason { get; set; } = "";

    public Dictionary<string, string> Record { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ReconcileStatistics
{
    public int TotalRecords { get; set; }

    public int MatchedRecords { get; set; }

    public int UnresolvedRecords { get; set; }

    public int AmbiguousRecords { get; set; }

    public int MatchedGroups { get; set; }
}

public sealed class ReconcileResult
{
    public List<MatchGroupResult> MatchedGroups { get; set; } = [];

    public List<UnresolvedResult> Unresolved { get; set; } = [];

    public ReconcileStatistics Statistics { get; set; } = new();
}

public static class ProfileJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static readonly JsonSerializerOptions Storage = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static ProfileDefinition Parse(string json)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<ProfileDefinition>(json, Options);
            if (profile == null)
            {
                throw new ReconciliationException("Profile must contain a rules array");
            }

            profile.Datasets ??= [];
            profile.Rules ??= [];
            foreach (var rule in profile.Rules)
            {
                rule.Datasets ??= [];
                rule.Keys ??= [];
                foreach (var key in rule.Keys)
                {
                    key.Fields ??= new Dictionary<string, string>(StringComparer.Ordinal);
                }
            }

            return profile;
        }
        catch (JsonException ex)
        {
            throw new ReconciliationException("Profile JSON is not valid: " + ex.Message);
        }
    }

    public static string Write(ProfileDefinition profile)
    {
        return JsonSerializer.Serialize(profile, Options);
    }
}

public sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetDecimal(out var number)
                ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            _ => throw new JsonException("Expected a string or number."),
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
