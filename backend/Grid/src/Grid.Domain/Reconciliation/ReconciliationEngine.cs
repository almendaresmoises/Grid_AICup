using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Grid.Reconciliation;

/// <summary>
/// Deterministic matcher. It never calls a model and never evaluates generated code.
/// </summary>
public static class ReconciliationEngine
{
    public const int MaxRows = 100_000;

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "MM/dd/yyyy", "yyyy/MM/dd", "dd-MMM-yyyy"];
    private static readonly Regex RuleIdPattern = new("^[A-Za-z0-9_-]{1,40}$", RegexOptions.CultureInvariant);

    public static List<Dictionary<string, string>> ReadCsv(string text, int maxRows = MaxRows)
    {
        if (text.StartsWith('\uFEFF'))
        {
            text = text[1..];
        }

        var records = ParseRecords(text);
        if (records.Count == 0 || records[0].Count == 0)
        {
            throw new ReconciliationException("CSV requires a header row");
        }

        var headers = records[0];
        if (headers.Count != headers.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ReconciliationException("CSV contains duplicate column names");
        }

        if (headers.Any(header => string.IsNullOrWhiteSpace(header)))
        {
            throw new ReconciliationException("CSV contains an empty column header");
        }

        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < records.Count; i++)
        {
            if (rows.Count >= maxRows)
            {
                throw new ReconciliationException($"CSV exceeds the limit of {maxRows:N0} rows");
            }

            var record = records[i];
            if (record.Count == 1 && record[0].Length == 0)
            {
                continue;
            }

            if (record.Count != headers.Count)
            {
                throw new ReconciliationException($"Malformed CSV row {i + 1}");
            }

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var column = 0; column < headers.Count; column++)
            {
                row[headers[column]] = record[column];
            }

            rows.Add(row);
        }

        return rows;
    }

    public static string InferColumnType(IEnumerable<string> values)
    {
        var present = values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(20).ToList();
        if (present.Count == 0)
        {
            return "text";
        }

        if (present.All(value => CanonicalAmount(value) != null))
        {
            return "amount";
        }

        if (present.All(value => CanonicalDate(value) != null))
        {
            return "date";
        }

        return "text";
    }

    public static void ValidateProfile(
        ProfileDefinition profile,
        Dictionary<string, List<Dictionary<string, string>>> datasets)
    {
        if (profile.Rules == null)
        {
            throw new ReconciliationException("Profile must contain a rules array");
        }

        var codes = datasets.Keys.ToList();
        if (codes.Count is not (2 or 3) || codes.Distinct(StringComparer.Ordinal).Count() != codes.Count)
        {
            throw new ReconciliationException("Supply exactly 2 or 3 unique dataset codes");
        }

        if (profile.Datasets == null ||
            profile.Datasets.ToHashSet(StringComparer.Ordinal).SetEquals(codes) == false ||
            profile.Datasets.Count != codes.Count)
        {
            throw new ReconciliationException("Saved dataset codes do not match uploaded dataset codes");
        }

        var schemas = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (code, rows) in datasets)
        {
            if (rows.Count == 0)
            {
                throw new ReconciliationException("Each CSV must contain at least one data row");
            }

            schemas[code] = rows[0].Keys.ToHashSet(StringComparer.Ordinal);
        }

        if (profile.Rules.Count is < 1 or > 12)
        {
            throw new ReconciliationException("Provide 1–12 matching rules");
        }

        var ruleIds = new List<string>();
        foreach (var rule in profile.Rules)
        {
            if (rule.Type is not ("exact" or "aggregate"))
            {
                throw new ReconciliationException("Only exact and aggregate rules are supported");
            }

            if (string.IsNullOrEmpty(rule.Id) || !RuleIdPattern.IsMatch(rule.Id))
            {
                throw new ReconciliationException("Rule id must be a short alphanumeric identifier");
            }

            ruleIds.Add(rule.Id);
            var participating = rule.Datasets ?? [];
            if (participating.Count is < 2 or > 3 || participating.Distinct(StringComparer.Ordinal).Count() != participating.Count)
            {
                throw new ReconciliationException("Each rule must name 2 or 3 unique datasets");
            }

            if (!participating.All(code => codes.Contains(code, StringComparer.Ordinal)))
            {
                throw new ReconciliationException("Rule references an unknown dataset");
            }

            var keys = rule.Keys ?? [];
            if (keys.Count is < 1 or > 8)
            {
                throw new ReconciliationException("Each rule must have 1–8 key specifications");
            }

            foreach (var key in keys)
            {
                ValidateKey(key, participating, schemas);
            }

            if (rule.Type == "aggregate")
            {
                if (participating.Count != 2 ||
                    string.IsNullOrEmpty(rule.Target) ||
                    string.IsNullOrEmpty(rule.Source) ||
                    !participating.Contains(rule.Target, StringComparer.Ordinal) ||
                    !participating.Contains(rule.Source, StringComparer.Ordinal) ||
                    rule.Target == rule.Source)
                {
                    throw new ReconciliationException("Aggregate requires distinct target and source datasets");
                }

                if (rule.TargetAmount == null || !schemas[rule.Target].Contains(rule.TargetAmount))
                {
                    throw new ReconciliationException($"Unknown {rule.Target}.target_amount");
                }

                if (rule.SourceAmount == null || !schemas[rule.Source].Contains(rule.SourceAmount))
                {
                    throw new ReconciliationException($"Unknown {rule.Source}.source_amount");
                }

                if (!decimal.TryParse(rule.Tolerance ?? "0", NumberStyles.Number, CultureInfo.InvariantCulture, out var tolerance))
                {
                    throw new ReconciliationException("Invalid aggregation tolerance");
                }

                if (tolerance < 0)
                {
                    throw new ReconciliationException("Tolerance must be finite and non-negative");
                }
            }
        }

        if (ruleIds.Count != ruleIds.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ReconciliationException("Rule IDs must be unique");
        }
    }

    public static ReconcileResult Reconcile(
        Dictionary<string, List<Dictionary<string, string>>> datasets,
        ProfileDefinition profile)
    {
        ValidateProfile(profile, datasets);

        var available = datasets.ToDictionary(pair => pair.Key, pair => pair.Value.Select((_, index) => index).ToHashSet(), StringComparer.Ordinal);
        var ambiguous = datasets.Keys.ToDictionary(code => code, _ => new HashSet<int>(), StringComparer.Ordinal);
        var matched = new List<MatchGroupResult>();

        foreach (var rule in profile.Rules)
        {
            var signatures = new Dictionary<string, Dictionary<string[], List<int>>>(StringComparer.Ordinal);
            foreach (var code in rule.Datasets)
            {
                var byKey = new Dictionary<string[], List<int>>(SignatureComparer.Instance);
                foreach (var index in available[code].OrderBy(value => value))
                {
                    var signature = Signature(datasets[code][index], rule, code);
                    if (signature == null)
                    {
                        continue;
                    }

                    if (!byKey.TryGetValue(signature, out var indexes))
                    {
                        indexes = [];
                        byKey[signature] = indexes;
                    }

                    indexes.Add(index);
                }

                signatures[code] = byKey;
            }

            HashSet<string[]>? common = null;
            foreach (var code in rule.Datasets)
            {
                var keys = new HashSet<string[]>(signatures[code].Keys, SignatureComparer.Instance);
                if (common == null)
                {
                    common = keys;
                }
                else
                {
                    common.IntersectWith(keys);
                }
            }

            foreach (var signature in (common ?? []).OrderBy(value => value, SignatureComparer.Instance))
            {
                var indexes = rule.Datasets.ToDictionary(code => code, code => signatures[code][signature], StringComparer.Ordinal);
                EvidenceResult evidence;
                if (rule.Type == "exact")
                {
                    if (indexes.Values.Any(ids => ids.Count != 1))
                    {
                        MarkAmbiguous(ambiguous, indexes);
                        continue;
                    }

                    evidence = new EvidenceResult { Comparison = "equal_normalized_keys" };
                }
                else
                {
                    var target = rule.Target!;
                    var source = rule.Source!;
                    if (indexes[target].Count != 1)
                    {
                        MarkAmbiguous(ambiguous, indexes);
                        continue;
                    }

                    var expected = ParseAmount(datasets[target][indexes[target][0]][rule.TargetAmount!]);
                    var sourceValues = indexes[source]
                        .Select(index => ParseAmount(datasets[source][index][rule.SourceAmount!]))
                        .ToList();
                    if (expected == null || sourceValues.Any(value => value == null))
                    {
                        continue;
                    }

                    var actual = sourceValues.Aggregate(0m, (sum, value) => sum + value!.Value);
                    var variance = actual - expected.Value;
                    var tolerance = decimal.Parse(rule.Tolerance ?? "0", CultureInfo.InvariantCulture);
                    if (Math.Abs(variance) > tolerance)
                    {
                        continue;
                    }

                    evidence = new EvidenceResult
                    {
                        Comparison = "sum",
                        Target = expected.Value.ToString(CultureInfo.InvariantCulture),
                        SourceSum = actual.ToString(CultureInfo.InvariantCulture),
                        Variance = variance.ToString(CultureInfo.InvariantCulture),
                    };
                }

                matched.Add(new MatchGroupResult
                {
                    Rule = rule.Id,
                    Key = signature.ToList(),
                    Records = indexes.ToDictionary(pair => pair.Key, pair => pair.Value.Select(index => index + 2).ToList(), StringComparer.Ordinal),
                    Evidence = evidence,
                });

                foreach (var (code, ids) in indexes)
                {
                    available[code].ExceptWith(ids);
                }
            }
        }

        var unresolved = new List<UnresolvedResult>();
        foreach (var (code, rows) in datasets)
        {
            foreach (var index in available[code].OrderBy(value => value))
            {
                unresolved.Add(new UnresolvedResult
                {
                    Dataset = code,
                    CsvRow = index + 2,
                    Reason = ambiguous[code].Contains(index) ? "ambiguous" : "unmatched",
                    Record = new Dictionary<string, string>(rows[index], StringComparer.Ordinal),
                });
            }
        }

        return new ReconcileResult
        {
            MatchedGroups = matched,
            Unresolved = unresolved,
            Statistics = new ReconcileStatistics
            {
                TotalRecords = datasets.Sum(pair => pair.Value.Count),
                MatchedRecords = matched.Sum(group => group.Records.Sum(pair => pair.Value.Count)),
                UnresolvedRecords = unresolved.Count,
                AmbiguousRecords = unresolved.Count(row => row.Reason == "ambiguous"),
                MatchedGroups = matched.Count,
            },
        };
    }

    public static bool PreservesMatches(ReconcileResult previous, ReconcileResult proposed)
    {
        var proposedKeys = proposed.MatchedGroups.Select(GroupKey).ToHashSet(StringComparer.Ordinal);
        return previous.MatchedGroups.Select(GroupKey).All(proposedKeys.Contains);
    }

    public static string GroupKey(MatchGroupResult group)
    {
        return string.Join(
            "||",
            group.Records
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + string.Join(",", pair.Value)));
    }

    private static void ValidateKey(
        KeyDefinition key,
        IReadOnlyList<string> datasets,
        IReadOnlyDictionary<string, HashSet<string>> schemas)
    {
        if (key.Type is not ("text" or "amount" or "date"))
        {
            throw new ReconciliationException("Key must declare type: text, amount, or date");
        }

        var fields = key.Fields ?? new Dictionary<string, string>();
        if (!fields.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(datasets))
        {
            throw new ReconciliationException($"Key fields must name every participating dataset: {string.Join(", ", datasets)}");
        }

        var prefixes = key.StripPrefixes ?? new Dictionary<string, List<string>>();
        if (prefixes.Keys.Any(code => !datasets.Contains(code, StringComparer.Ordinal)))
        {
            throw new ReconciliationException("strip_prefixes must map participating dataset codes");
        }

        foreach (var (code, name) in fields)
        {
            if (string.IsNullOrEmpty(name) || !schemas[code].Contains(name))
            {
                throw new ReconciliationException($"Unknown column: {code}.{name}");
            }
        }

        foreach (var (code, choices) in prefixes)
        {
            if (key.Type != "text" || choices == null || choices.Count is < 1 or > 12)
            {
                throw new ReconciliationException("Prefix normalization accepts 1–12 text prefixes per dataset");
            }

            if (choices.Any(prefix => string.IsNullOrEmpty(prefix) || prefix.Length > 60))
            {
                throw new ReconciliationException("Invalid prefix normalization value");
            }
        }
    }

    private static string[]? Signature(IReadOnlyDictionary<string, string> row, RuleDefinition rule, string dataset)
    {
        var values = new string[rule.Keys.Count];
        for (var i = 0; i < rule.Keys.Count; i++)
        {
            var key = rule.Keys[i];
            if (!row.TryGetValue(key.Fields[dataset], out var raw))
            {
                return null;
            }

            var normalized = Normalize(raw, key, dataset);
            if (normalized == null)
            {
                return null;
            }

            values[i] = normalized;
        }

        return values;
    }

    private static string? Normalize(string? value, KeyDefinition key, string dataset)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (key.Type == "text")
        {
            if (key.StripPrefixes != null && key.StripPrefixes.TryGetValue(dataset, out var prefixes))
            {
                foreach (var prefix in prefixes)
                {
                    if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        value = value[prefix.Length..].Trim();
                        break;
                    }
                }
            }

            return value.Length == 0 ? null : value.ToUpperInvariant();
        }

        if (key.Type == "amount")
        {
            return CanonicalAmount(value);
        }

        if (key.Type == "date")
        {
            return CanonicalDate(value);
        }

        return null;
    }

    private static string? CanonicalAmount(string value)
    {
        var amount = ParseAmount(value);
        if (amount == null)
        {
            return null;
        }

        return amount.Value.ToString("0.#############################", CultureInfo.InvariantCulture);
    }

    private static decimal? ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Replace(",", "", StringComparison.Ordinal)
            .Replace("₱", "", StringComparison.Ordinal)
            .Replace("$", "", StringComparison.Ordinal)
            .Trim();
        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        return amount;
    }

    private static string? CanonicalDate(string value)
    {
        if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static void MarkAmbiguous(Dictionary<string, HashSet<int>> ambiguous, Dictionary<string, List<int>> indexes)
    {
        foreach (var (code, ids) in indexes)
        {
            foreach (var id in ids)
            {
                ambiguous[code].Add(id);
            }
        }
    }

    private static List<List<string>> ParseRecords(string text)
    {
        var records = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (inQuotes)
            {
                if (current == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(current);
                }

                continue;
            }

            if (current == '"')
            {
                inQuotes = true;
            }
            else if (current == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (current == '\n')
            {
                FinishRow(records, row, field);
                row = [];
            }
            else if (current != '\r')
            {
                field.Append(current);
            }
        }

        if (inQuotes)
        {
            throw new ReconciliationException("Malformed CSV row");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            FinishRow(records, row, field);
        }

        return records;
    }

    private static void FinishRow(List<List<string>> records, List<string> row, StringBuilder field)
    {
        if (field.Length == 0 && row.Count == 0)
        {
            return;
        }

        row.Add(field.ToString());
        field.Clear();
        records.Add(row);
    }

    private sealed class SignatureComparer : IEqualityComparer<string[]>, IComparer<string[]>
    {
        public static readonly SignatureComparer Instance = new();

        public bool Equals(string[]? x, string[]? y)
        {
            if (x == null || y == null || x.Length != y.Length)
            {
                return false;
            }

            for (var i = 0; i < x.Length; i++)
            {
                if (!string.Equals(x[i], y[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(string[] obj)
        {
            var hash = new HashCode();
            foreach (var part in obj)
            {
                hash.Add(part, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }

        public int Compare(string[]? x, string[]? y)
        {
            if (x == null || y == null)
            {
                return 0;
            }

            var length = Math.Min(x.Length, y.Length);
            for (var i = 0; i < length; i++)
            {
                var compared = string.CompareOrdinal(x[i], y[i]);
                if (compared != 0)
                {
                    return compared;
                }
            }

            return x.Length.CompareTo(y.Length);
        }
    }
}
