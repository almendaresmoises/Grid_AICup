using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grid.Datasets;

public static class DatasetContractValidator
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
    {
        "string", "date", "datetime", "time", "decimal", "integer", "int32", "int64", "float", "boolean", "binary", "json"
    };

    private static readonly Regex PrefixPattern = new("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);

    public static string? Validate(string code, string name, string gcsPrefix, string schemaJson, string partitionJson)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            return "Dataset name and code are required.";
        if (!PrefixPattern.IsMatch(gcsPrefix.Trim()))
            return "Dataset directory name may contain only letters, numbers, underscores, and hyphens.";
        if (!TryReadContract(schemaJson, out var fields) || fields.Count == 0)
            return "Dataset schema must be a valid JSON array with at least one named field.";
        if (fields.Any(field => string.IsNullOrWhiteSpace(field.Name)) ||
            fields.Select(field => field.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Count)
            return "Dataset schema field names must be non-empty and unique.";
        if (fields.Any(field => !AllowedTypes.Contains(field.Type)))
            return "Dataset schema contains an unsupported column type.";

        var semanticFields = fields.Where(field => field.SemanticRole != "none").ToArray();
        if (semanticFields.Select(field => field.SemanticRole).Distinct(StringComparer.Ordinal).Count() != semanticFields.Length)
            return "Each reconciliation semantic role can be assigned to only one dataset column.";

        var businessDate = semanticFields.SingleOrDefault(field => field.SemanticRole == "business_date");
        if (businessDate is not null && (businessDate.Type != "date" || !businessDate.IsRequired))
            return "The Business Date column must use pl.Date and be required.";
        var company = semanticFields.SingleOrDefault(field => field.SemanticRole == "company_code");
        if (company is not null && (company.Type != "string" || !company.IsRequired))
            return "The Company Code column must use pl.String and be required to preserve leading zeroes.";
        var branch = semanticFields.SingleOrDefault(field => field.SemanticRole == "branch_code");
        if (branch is not null && branch.Type != "string")
            return "The Branch Code column must use pl.String to preserve leading zeroes.";
        var recordId = semanticFields.SingleOrDefault(field => field.SemanticRole == "record_id");
        if (recordId is not null && (recordId.Type != "string" || !recordId.IsRequired))
            return "The Record ID column must use pl.String and be required.";

        if (!TryReadStringArray(partitionJson, out var partitions))
            return "Dataset partitions must be a valid JSON string array.";
        if (partitions.Any(string.IsNullOrWhiteSpace) || partitions.Count != partitions.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "Dataset partition field names must be non-empty and unique.";

        var hasDatePartitions = new[] { "year", "month", "day" }.All(name => partitions.Any(partition => string.Equals(partition, name, StringComparison.OrdinalIgnoreCase)));
        if (hasDatePartitions && !fields.Any(field => field.Type == "date" || field.SemanticRole == "business_date" || string.Equals(field.Name, "trx_date", StringComparison.OrdinalIgnoreCase) || string.Equals(field.Name, "date", StringComparison.OrdinalIgnoreCase)))
            return "Date partitions (year, month, day) require at least one pl.Date schema column.";

        return null;
    }

    public static bool TryReadContract(string json, out List<DatasetContractField> values)
    {
        values = [];
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                    return false;
                var rawType = item.TryGetProperty("polarsType", out var polarsType) && polarsType.ValueKind == JsonValueKind.String
                    ? polarsType.GetString() ?? ""
                    : item.TryGetProperty("type", out var legacyType) && legacyType.ValueKind == JsonValueKind.String
                        ? legacyType.GetString() ?? ""
                        : "string";
                var required = item.TryGetProperty("isRequired", out var isRequired) && isRequired.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? isRequired.GetBoolean()
                    : item.TryGetProperty("nullable", out var nullable) && nullable.ValueKind is JsonValueKind.True or JsonValueKind.False && !nullable.GetBoolean();
                var role = item.TryGetProperty("semanticRole", out var semanticRole) && semanticRole.ValueKind == JsonValueKind.String
                    ? semanticRole.GetString() ?? "none"
                    : "none";
                if (role is not ("none" or "business_date" or "company_code" or "branch_code" or "record_id")) return false;
                values.Add(new DatasetContractField(name.GetString()?.Trim() ?? "", NormalizeType(rawType), required, role));
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryReadStringArray(string json, out List<string> values)
    {
        values = [];
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) return false;
                values.Add(item.GetString()?.Trim() ?? "");
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string NormalizeType(string value)
    {
        var name = value.Replace("pl.", "", StringComparison.Ordinal).ToLowerInvariant();
        if (name.StartsWith("decimal", StringComparison.Ordinal)) return "decimal";
        if (name.StartsWith("datetime", StringComparison.Ordinal)) return "datetime";
        return name switch
        {
            "utf8" => "string",
            "int8" or "int16" or "uint8" or "uint16" or "uint32" or "uint64" => "integer",
            "float32" or "float64" => "float",
            _ => name
        };
    }
}

public sealed record DatasetContractField(string Name, string Type, bool IsRequired, string SemanticRole);
