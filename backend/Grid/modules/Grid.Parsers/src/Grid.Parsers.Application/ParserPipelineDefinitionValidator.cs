using System.Text.Json;

namespace Grid.Parsers;

public static class ParserPipelineDefinitionValidator
{
    private static readonly HashSet<string> AllowedOperations = new(StringComparer.Ordinal)
    {
        "select", "dropColumns", "rename", "cast", "derive", "filter", "dropNullRows", "fillNull",
        "textFormat", "trimCharacters", "replaceText", "mask", "substring", "regexExtract", "split", "concatColumns",
        "deduplicate", "sort", "groupAggregate", "pivot", "unpivot", "transpose",
        "join", "fuzzyJoin", "union", "parseDateTime", "datePart", "duration",
        "parseJsonColumn", "unnestStruct", "explodeList", "profile", "qualityRule"
    };

    private static readonly HashSet<string> AllowedSinks = new(StringComparer.Ordinal) { "parquet", "gcs" };

    public static string? Validate(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "Parser definition is required.";
        if (json.Length > 2 * 1024 * 1024) return "Parser definition exceeds the 2 MiB limit.";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "Parser definition must be a JSON object.";
            if (!root.TryGetProperty("dslVersion", out var version) || version.GetString() is not ("2.0" or "2.1"))
                return "Parser definition must use DSL 2.0 or 2.1.";
            if (!root.TryGetProperty("pipeline", out var pipeline) || pipeline.ValueKind != JsonValueKind.Object)
                return "DSL 2.0 requires pipeline identity.";
            if (!root.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Array || inputs.GetArrayLength() is < 1 or > 20)
                return "DSL 2.0 requires between 1 and 20 inputs.";
            var inputIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in inputs.EnumerateArray())
            {
                if (!input.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
                    return "Every parser input requires an id.";
                if (!inputIds.Add(id.GetString()!)) return $"Duplicate parser input id '{id.GetString()}'.";
            }
            if (!inputIds.Contains("main")) return "DSL 2.0 requires a 'main' input.";
            if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() > 500)
                return "DSL 2.0 steps must be an array with at most 500 entries.";
            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var step in steps.EnumerateArray())
            {
                if (!step.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
                    return "Every parser step requires an id.";
                if (!stepIds.Add(id.GetString()!)) return $"Duplicate parser step id '{id.GetString()}'.";
                if (!step.TryGetProperty("op", out var operation) || !AllowedOperations.Contains(operation.GetString() ?? ""))
                    return $"Unsupported parser operation '{operation.GetString()}'.";
                if (!step.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Object)
                    return $"Parser step '{id.GetString()}' requires an args object.";
            }
            if (!root.TryGetProperty("sink", out var sink) || sink.ValueKind != JsonValueKind.Object)
                return "DSL 2.0 requires a sink.";
            if (sink.TryGetProperty("connector", out var connector) && !AllowedSinks.Contains(connector.GetString() ?? ""))
                return "Parser runs publish Parquet output to Cloud Storage. Select a parquet or gcs sink.";
            if (sink.TryGetProperty("writeMode", out var writeMode) && writeMode.GetString() is not ("overwrite" or "existingIdempotent"))
                return "Parser runs own an isolated output snapshot. Choose overwrite or existingIdempotent.";
            if (!sink.TryGetProperty("binding", out var binding) || binding.GetString() != "execution.output")
                return "Parser sink binding must be 'execution.output'.";
            return null;
        }
        catch (JsonException exception)
        {
            return $"Parser definition is not valid JSON: {exception.Message}";
        }
    }
}
