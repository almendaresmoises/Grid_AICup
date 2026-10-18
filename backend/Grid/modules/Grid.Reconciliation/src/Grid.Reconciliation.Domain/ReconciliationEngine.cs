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
            if (rows.Count > MaxRows) throw new ReconciliationException($"Combined files for {code} exceed {MaxRows:N0} rows.");
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
            if (rule.Type is not ("exact" or "aggregate" or "partial"))
            {
                throw new ReconciliationException("Only exact, aggregate, and partial rules are supported");
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

            if (rule.Type == "partial")
            {
                if (rule.MinScore.HasValue && (rule.MinScore.Value < 0m || rule.MinScore.Value > 1m))
                {
                    throw new ReconciliationException("Partial rule min_score must be between 0.0 and 1.0");
                }

                if (keys.Count < 2)
                {
                    throw new ReconciliationException("Partial rules require at least 2 keys (anchor exact key and partial/divergent key)");
                }
                if (!keys.Any(key => key.MatchMode is null or "exact" && key.ExpectedMismatch != true))
                    throw new ReconciliationException("Partial rules require at least one exact anchor key.");
                if (!keys.Any(key => key.MatchMode is "partial" or "fuzzy" or "divergent" || key.ExpectedMismatch == true))
                    throw new ReconciliationException("Partial rules must explicitly declare the key allowed to differ.");
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
            if (rule.Type == "partial")
            {
                var fullRule = profile.Rules.FirstOrDefault(candidate => candidate.Type == "exact" && candidate.Datasets.ToHashSet().SetEquals(rule.Datasets));
                ReconcilePartialRule(rule, datasets, available, ambiguous, matched, fullRule);
                continue;
            }

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

    private static void ReconcilePartialRule(
        RuleDefinition rule,
        IReadOnlyDictionary<string, List<Dictionary<string, string>>> datasets,
        Dictionary<string, HashSet<int>> available,
        Dictionary<string, HashSet<int>> ambiguous,
        List<MatchGroupResult> matched,
        RuleDefinition? fullRule)
    {
        var omitted = fullRule?.Keys.Where(required => !rule.Keys.Any(key => key.Type == required.Type &&
            required.Fields.All(field => key.Fields.GetValueOrDefault(field.Key) == field.Value))).ToList() ?? [];
        var omittedRemarks = "Full-match requirements not evaluated: " +
            string.Join(", ", omitted.Select(key => key.Fields[rule.Datasets[0]]));
        var exactIndices = new List<int>();
        var partialIndices = new List<int>();

        for (var i = 0; i < rule.Keys.Count; i++)
        {
            var key = rule.Keys[i];
            var isPartial = key.MatchMode is "partial" or "fuzzy" or "divergent" || key.ExpectedMismatch == true;
            if (isPartial)
            {
                partialIndices.Add(i);
            }
            else
            {
                exactIndices.Add(i);
            }
        }

        var defaultMinScore = rule.MinScore ?? 0.80m;

        var signatures = new Dictionary<string, Dictionary<string[], List<int>>>(StringComparer.Ordinal);
        foreach (var code in rule.Datasets)
        {
            var byKey = new Dictionary<string[], List<int>>(SignatureComparer.Instance);
            foreach (var index in available[code].OrderBy(value => value))
            {
                var sig = ExactSignature(datasets[code][index], rule, exactIndices, code);
                if (sig == null)
                {
                    continue;
                }

                if (!byKey.TryGetValue(sig, out var indexes))
                {
                    indexes = [];
                    byKey[sig] = indexes;
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

        foreach (var exactSig in (common ?? []).OrderBy(value => value, SignatureComparer.Instance))
        {
            var candidateMap = rule.Datasets.ToDictionary(code => code, code => signatures[code][exactSig], StringComparer.Ordinal);
            var combinations = candidateMap.Values.Aggregate(1L, (count, ids) => Math.Min(1_000_001L, count * ids.Count));
            if (combinations > 100_000)
                throw new ReconciliationException($"{rule.Id} has too many competing candidates. Add stronger exact anchors.");

            if (rule.Datasets.Count == 2)
            {
                var codeA = rule.Datasets[0];
                var codeB = rule.Datasets[1];
                var rowsA = candidateMap[codeA].Where(id => available[codeA].Contains(id)).ToList();
                var rowsB = candidateMap[codeB].Where(id => available[codeB].Contains(id)).ToList();

                var scoredPairs = new List<(int idxA, int idxB, decimal score, string remarks, string? divergentKey)>();

                foreach (var idxA in rowsA)
                {
                    var rowA = datasets[codeA][idxA];
                    foreach (var idxB in rowsB)
                    {
                        var rowB = datasets[codeB][idxB];
                        var (passed, score, remarks, divergentKey) = EvaluatePartialKeys(
                            rowA, rowB, rule, partialIndices, codeA, codeB, defaultMinScore);

                        if (passed)
                        {
                            scoredPairs.Add((idxA, idxB, score, remarks, divergentKey));
                        }
                    }
                }

                scoredPairs = scoredPairs.OrderByDescending(p => p.score).ToList();

                var usedA = new HashSet<int>();
                var usedB = new HashSet<int>();

                foreach (var pair in scoredPairs)
                {
                    if (usedA.Contains(pair.idxA) || usedB.Contains(pair.idxB))
                    {
                        continue;
                    }

                    var competingA = scoredPairs.Where(p => p.idxA == pair.idxA && !usedB.Contains(p.idxB)).ToList();
                    var competingB = scoredPairs.Where(p => p.idxB == pair.idxB && !usedA.Contains(p.idxA)).ToList();
                    if ((competingA.Count > 1 && competingA[0].score == competingA[1].score && competingA[0].idxB != competingA[1].idxB) ||
                        (competingB.Count > 1 && competingB[0].score == competingB[1].score && competingB[0].idxA != competingB[1].idxA))
                    {
                        MarkAmbiguous(ambiguous, new Dictionary<string, List<int>> { [codeA] = [pair.idxA], [codeB] = [pair.idxB] });
                        continue;
                    }

                    usedA.Add(pair.idxA);
                    usedB.Add(pair.idxB);

                    var fullKey = BuildFullKey(datasets[codeA][pair.idxA], rule, codeA);

                    matched.Add(new MatchGroupResult
                    {
                        Rule = rule.Id,
                        RuleType = pair.divergentKey == null && omitted.Count == 0 ? "exact" : "partial",
                        Key = fullKey,
                        Records = new Dictionary<string, List<int>>
                        {
                            [codeA] = [pair.idxA + 2],
                            [codeB] = [pair.idxB + 2],
                        },
                        Evidence = new EvidenceResult
                        {
                            Comparison = pair.divergentKey == null && omitted.Count == 0 ? "equal_normalized_keys" : "partial_match",
                            Remarks = omitted.Count > 0 ? omittedRemarks + "; " + pair.remarks : pair.divergentKey == null ? "All configured keys match exactly" : pair.remarks,
                            Score = omitted.Count > 0 || rule.Keys.Any(k => k.ExpectedMismatch == true || k.MatchMode == "divergent") ? null : pair.score,
                            DivergentKey = pair.divergentKey,
                        },
                        Remarks = omitted.Count > 0 ? omittedRemarks + "; " + pair.remarks : pair.divergentKey == null ? "All configured keys match exactly" : pair.remarks,
                        Score = omitted.Count > 0 || rule.Keys.Any(k => k.ExpectedMismatch == true || k.MatchMode == "divergent") ? null : pair.score,
                    });

                    available[codeA].Remove(pair.idxA);
                    available[codeB].Remove(pair.idxB);
                }
            }
            else if (rule.Datasets.Count == 3)
            {
                var codeA = rule.Datasets[0];
                var codeB = rule.Datasets[1];
                var codeC = rule.Datasets[2];

                var rowsA = candidateMap[codeA].Where(id => available[codeA].Contains(id)).ToList();
                var rowsB = candidateMap[codeB].Where(id => available[codeB].Contains(id)).ToList();
                var rowsC = candidateMap[codeC].Where(id => available[codeC].Contains(id)).ToList();

                var scoredTriplets = new List<(int idxA, int idxB, int idxC, decimal score, string remarks, string? divergentKey)>();

                foreach (var idxA in rowsA)
                {
                    var rowA = datasets[codeA][idxA];
                    foreach (var idxB in rowsB)
                    {
                        var rowB = datasets[codeB][idxB];
                        var (passedAB, scoreAB, remarksAB, divAB) = EvaluatePartialKeys(
                            rowA, rowB, rule, partialIndices, codeA, codeB, defaultMinScore);
                        if (!passedAB)
                        {
                            continue;
                        }

                        foreach (var idxC in rowsC)
                        {
                            var rowC = datasets[codeC][idxC];
                            var (passedAC, scoreAC, remarksAC, divAC) = EvaluatePartialKeys(
                                rowA, rowC, rule, partialIndices, codeA, codeC, defaultMinScore);
                            if (!passedAC)
                            {
                                continue;
                            }

                            var (passedBC, scoreBC, remarksBC, divBC) = EvaluatePartialKeys(
                                rowB, rowC, rule, partialIndices, codeB, codeC, defaultMinScore);
                            if (!passedBC) continue;
                            var totalScore = Math.Round((scoreAB + scoreAC + scoreBC) / 3m, 4);
                            var remarks = string.Join("; ", new[] { $"{codeA}/{codeB}: {remarksAB}", $"{codeA}/{codeC}: {remarksAC}", $"{codeB}/{codeC}: {remarksBC}" });
                            var div = divAB ?? divAC ?? divBC;
                            scoredTriplets.Add((idxA, idxB, idxC, totalScore, remarks, div));
                        }
                    }
                }

                scoredTriplets = scoredTriplets.OrderByDescending(t => t.score).ToList();
                var usedA = new HashSet<int>();
                var usedB = new HashSet<int>();
                var usedC = new HashSet<int>();

                foreach (var triplet in scoredTriplets)
                {
                    if (usedA.Contains(triplet.idxA) || usedB.Contains(triplet.idxB) || usedC.Contains(triplet.idxC))
                    {
                        continue;
                    }
                    if (scoredTriplets.Any(other => other.score == triplet.score && other != triplet &&
                        !usedA.Contains(other.idxA) && !usedB.Contains(other.idxB) && !usedC.Contains(other.idxC) &&
                        (other.idxA == triplet.idxA || other.idxB == triplet.idxB || other.idxC == triplet.idxC)))
                    {
                        MarkAmbiguous(ambiguous, new Dictionary<string, List<int>> { [codeA] = [triplet.idxA], [codeB] = [triplet.idxB], [codeC] = [triplet.idxC] });
                        continue;
                    }

                    usedA.Add(triplet.idxA);
                    usedB.Add(triplet.idxB);
                    usedC.Add(triplet.idxC);

                    var fullKey = BuildFullKey(datasets[codeA][triplet.idxA], rule, codeA);

                    matched.Add(new MatchGroupResult
                    {
                        Rule = rule.Id,
                        RuleType = triplet.divergentKey == null && omitted.Count == 0 ? "exact" : "partial",
                        Key = fullKey,
                        Records = new Dictionary<string, List<int>>
                        {
                            [codeA] = [triplet.idxA + 2],
                            [codeB] = [triplet.idxB + 2],
                            [codeC] = [triplet.idxC + 2],
                        },
                        Evidence = new EvidenceResult
                        {
                            Comparison = triplet.divergentKey == null && omitted.Count == 0 ? "equal_normalized_keys" : "partial_match",
                            Remarks = omitted.Count > 0 ? omittedRemarks + "; " + triplet.remarks : triplet.divergentKey == null ? "All configured keys match exactly" : triplet.remarks,
                            Score = omitted.Count > 0 || rule.Keys.Any(k => k.ExpectedMismatch == true || k.MatchMode == "divergent") ? null : triplet.score,
                            DivergentKey = triplet.divergentKey,
                        },
                        Remarks = omitted.Count > 0 ? omittedRemarks + "; " + triplet.remarks : triplet.divergentKey == null ? "All configured keys match exactly" : triplet.remarks,
                        Score = omitted.Count > 0 || rule.Keys.Any(k => k.ExpectedMismatch == true || k.MatchMode == "divergent") ? null : triplet.score,
                    });

                    available[codeA].Remove(triplet.idxA);
                    available[codeB].Remove(triplet.idxB);
                    available[codeC].Remove(triplet.idxC);
                }
            }
        }
    }

    private static (bool passed, decimal score, string remarks, string? divergentKey) EvaluatePartialKeys(
        IReadOnlyDictionary<string, string> rowA,
        IReadOnlyDictionary<string, string> rowB,
        RuleDefinition rule,
        IReadOnlyList<int> partialIndices,
        string codeA,
        string codeB,
        decimal defaultMinScore)
    {
        if (partialIndices.Count == 0)
        {
            return (true, 1.0m, rule.Remarks ?? "Exact key match", null);
        }

        var scores = new List<decimal>();
        var remarksList = new List<string>();
        string? primaryDivergentKey = null;

        foreach (var p in partialIndices)
        {
            var keyDef = rule.Keys[p];
            var colA = keyDef.Fields.TryGetValue(codeA, out var fA) ? fA : "";
            var colB = keyDef.Fields.TryGetValue(codeB, out var fB) ? fB : "";
            var rawA = rowA.TryGetValue(colA, out var vA) ? vA : "";
            var rawB = rowB.TryGetValue(colB, out var vB) ? vB : "";

            if (string.IsNullOrWhiteSpace(rawA) || string.IsNullOrWhiteSpace(rawB))
                return (false, 0m, "", null);

            var normA = Normalize(rawA, keyDef, codeA) ?? rawA.Trim();
            var normB = Normalize(rawB, keyDef, codeB) ?? rawB.Trim();

            var isExpectedMismatch = keyDef.ExpectedMismatch == true || keyDef.MatchMode == "divergent";
            var minScore = keyDef.MinScore ?? defaultMinScore;

            if (isExpectedMismatch)
            {
                if (!string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase))
                {
                    primaryDivergentKey = colA;
                    remarksList.Add($"Possible match but {colA} is not matched ('{rawA}' vs '{rawB}')");
                }
                else
                {
                    remarksList.Add($"Expected mismatch on {colA} but values were identical ('{rawA}')");
                    scores.Add(1.0m);
                }
                continue;
            }

            decimal sim;
            if (keyDef.Type == "amount")
            {
                var decA = ParseAmount(normA);
                var decB = ParseAmount(normB);
                if (decA == null || decB == null)
                {
                    return (false, 0m, "", null);
                }

                decimal? tolerance = null;
                if (!string.IsNullOrEmpty(keyDef.Tolerance) && decimal.TryParse(keyDef.Tolerance, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedTol))
                {
                    tolerance = parsedTol;
                }

                if (tolerance.HasValue && Math.Abs(decA.Value - decB.Value) > tolerance.Value)
                    return (false, 0m, "", null);

                sim = NumericMatching.Similarity(decA.Value, decB.Value, tolerance);
            }
            else if (keyDef.Type == "date")
            {
                var dateA = CanonicalDate(normA);
                var dateB = CanonicalDate(normB);
                if (dateA == null || dateB == null)
                {
                    return (false, 0m, "", null);
                }

                if (DateTime.TryParse(dateA, out var dtA) && DateTime.TryParse(dateB, out var dtB))
                {
                    var daysDiff = Math.Abs((dtA - dtB).TotalDays);
                    var maxDays = decimal.TryParse(keyDef.Tolerance, NumberStyles.Number, CultureInfo.InvariantCulture, out var days) ? days : 2m;
                    if ((decimal)daysDiff > maxDays) return (false, 0m, "", null);
                    sim = daysDiff == 0 ? 1.0m : Math.Max(0m, 1.0m - ((decimal)daysDiff / 10m));
                }
                else
                {
                    sim = string.Equals(dateA, dateB, StringComparison.Ordinal) ? 1.0m : 0m;
                }
            }
            else
            {
                sim = DamerauLevenshtein.Similarity(normA, normB);
            }

            if (!string.Equals(normA, normB, StringComparison.Ordinal) && sim >= 1m) sim = 0.9999m;
            if (sim < minScore)
            {
                return (false, sim, "", null);
            }

            scores.Add(sim);
            if (!string.Equals(normA, normB, StringComparison.Ordinal))
            {
                primaryDivergentKey = colA;
                remarksList.Add($"Possible match: {colA} similarity is {sim * 100:0.#}% ('{rawA}' vs '{rawB}')");
            }
            else
            {
                remarksList.Add($"Matched on {colA}");
            }
        }

        var avgScore = scores.Count > 0 ? Math.Round(scores.Average(), 4) : 1.0m;
        if (rule.MinScore.HasValue && avgScore < rule.MinScore.Value)
        {
            return (false, avgScore, "", null);
        }

        var combinedRemarks = remarksList.Count > 0
            ? string.Join("; ", remarksList)
            : (rule.Remarks ?? "Partial match verified");

        return (true, avgScore, combinedRemarks, primaryDivergentKey);
    }

    private static string[]? ExactSignature(
        IReadOnlyDictionary<string, string> row,
        RuleDefinition rule,
        IReadOnlyList<int> exactKeyIndices,
        string dataset)
    {
        var values = new string[exactKeyIndices.Count];
        for (var i = 0; i < exactKeyIndices.Count; i++)
        {
            var key = rule.Keys[exactKeyIndices[i]];
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

    private static List<string> BuildFullKey(
        IReadOnlyDictionary<string, string> row,
        RuleDefinition rule,
        string dataset)
    {
        var result = new List<string>(rule.Keys.Count);
        foreach (var key in rule.Keys)
        {
            if (row.TryGetValue(key.Fields[dataset], out var raw))
            {
                var norm = Normalize(raw, key, dataset);
                result.Add(norm ?? raw.Trim());
            }
            else
            {
                result.Add("");
            }
        }
        return result;
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

        if (key.MinScore.HasValue && (key.MinScore.Value < 0m || key.MinScore.Value > 1m))
        {
            throw new ReconciliationException("Key min_score must be between 0.0 and 1.0");
        }

        if (!string.IsNullOrEmpty(key.MatchMode) && key.MatchMode is not ("exact" or "partial" or "fuzzy" or "divergent"))
        {
            throw new ReconciliationException("Key match_mode must be exact, partial, fuzzy, or divergent");
        }

        if (!string.IsNullOrEmpty(key.Algorithm) && key.Algorithm is not ("damerau_levenshtein" or "numeric_best_practice" or "exact" or "date_proximity"))
        {
            throw new ReconciliationException("Key algorithm must be damerau_levenshtein, numeric_best_practice, date_proximity, or exact");
        }
        if (key.Tolerance != null && (!decimal.TryParse(key.Tolerance, NumberStyles.Number, CultureInfo.InvariantCulture, out var tolerance) || tolerance < 0))
            throw new ReconciliationException("Key tolerance must be a non-negative number.");
        if (key.Algorithm == "damerau_levenshtein" && key.Type != "text" ||
            key.Algorithm == "numeric_best_practice" && key.Type != "amount" ||
            key.Algorithm == "date_proximity" && key.Type != "date")
            throw new ReconciliationException("Matching algorithm does not support the declared key type.");
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

    public static bool IsValidKeyValue(string? value, KeyDefinition key, string dataset) => Normalize(value, key, dataset) != null;

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

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fallbackParsed))
        {
            return fallbackParsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
