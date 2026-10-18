using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;

namespace Grid.Reconciliation;

public class ReconciliationOptimizationAdvisor : IReconciliationOptimizationAdvisor, ITransientDependency
{
    private readonly IGeminiClient _geminiClient;
    private readonly ILogger<ReconciliationOptimizationAdvisor> _logger;

    public ReconciliationOptimizationAdvisor(
        IGeminiClient geminiClient,
        ILogger<ReconciliationOptimizationAdvisor>? logger = null)
    {
        _geminiClient = geminiClient;
        _logger = logger ?? NullLogger<ReconciliationOptimizationAdvisor>.Instance;
    }

    public async Task<OptimizationAdvisorResult> AdviseAsync(OptimizationAdvisorInput input)
    {
        if (input.Manifest.HasNoUnresolved)
        {
            return new OptimizationAdvisorResult
            {
                Source = "deterministic",
                Diagnoses = input.Manifest.InitialDiagnoses,
                ChangeGroups = [],
            };
        }

        // Try model-backed advisor first
        try
        {
            var prompt = BuildAdvisorPrompt(input);
            var response = await _geminiClient.GenerateJsonAsync(prompt);
            var parsed = ParseModelResponse(response, input);
            if (parsed.ChangeGroups.Count > 0 || parsed.Diagnoses.Count > 0)
            {
                parsed.PromptModelProvenanceJson = JsonSerializer.Serialize(new
                {
                    promptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))),
                    source = "model",
                    timestamp = DateTime.UtcNow,
                });
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model-backed optimization advisor did not complete successfully. Falling back to deterministic advisor: {Message}", ex.Message);
        }

        return GenerateDeterministicAdvice(input);
    }

    private static string BuildAdvisorPrompt(OptimizationAdvisorInput input)
    {
        var existingRulesJson = JsonSerializer.Serialize(input.BaseProfile.Rules, ProfileJson.Options);
        var evidenceJson = JsonSerializer.Serialize(input.Manifest.StratifiedEvidence.Take(50), ProfileJson.Options);
        var datasetCodes = input.Rows.Keys.OrderBy(c => c).ToList();
        var datasetSchemas = datasetCodes.ToDictionary(
            c => c,
            c => input.Rows[c].Count > 0 ? input.Rows[c][0].Keys.ToList() : new List<string>());
        var schemaJson = JsonSerializer.Serialize(datasetSchemas, ProfileJson.Options);

        return $$"""
            You are an expert deterministic financial reconciliation optimization engineer.
            Examine the existing reconciliation profile, dataset schemas, and evidence of unmatched/ambiguous records.
            Propose safe, evidence-backed improvements to recover legitimate unmatched records without regressing existing matches.

            CRITICAL RULES:
            1. Existing rules and their order MUST REMAIN INTACT. Only APPEND new rule passes after existing ones.
            2. Any partial pass MUST have at least two exact anchor keys (e.g. amount + date, or reference + amount).
            3. Use only existing canonical columns present in the dataset schemas below. Do not invent columns.
            4. Each rule's 'datasets' array MUST name all participating dataset codes exactly: [{{string.Join(", ", datasetCodes.Select(c => $"\"{c}\""))}}].
            5. In each key's 'fields' dictionary, map EVERY participating dataset code to its corresponding column name in that dataset.
            6. For partial/fuzzy keys, use supported algorithms:
               - For dates: algorithm "date_proximity", tolerance "2" (meaning +/- 2 days)
               - For amounts: algorithm "numeric_best_practice", tolerance "1.00" (or small fee/penny variance)
               - For text/reference: algorithm "damerau_levenshtein", min_score 0.80 (for minor typos)
            7. Output a valid JSON object matching this structure:
            {
              "diagnoses": [
                {
                  "category": "FormatOrToleranceDifferences",
                  "summary": "Technical explanation of observed date lag, fee variance, or typos",
                  "affectedRecordCount": 10,
                  "sampleEvidenceIds": ["ev-1", "ev-2"]
                }
              ],
              "change_groups": [
                {
                  "groupId": "group-1",
                  "title": "Add settlement date tolerance pass (+/- 2 days)",
                  "safetyClass": "safe_additive",
                  "operations": [
                    {
                      "operationId": "op-1",
                      "changeType": "profile",
                      "targetIdentifier": "PARTIAL_DATE_TOL",
                      "operationType": "append_rule",
                      "operationJson": {
                        "id": "PARTIAL_DATE_TOL",
                        "type": "partial",
                        "datasets": [{{string.Join(", ", datasetCodes.Select(c => $"\"{c}\""))}}],
                        "keys": [
                          { "type": "text", "match_mode": "exact", "fields": { ... } },
                          { "type": "amount", "match_mode": "exact", "fields": { ... } },
                          { "type": "date", "match_mode": "fuzzy", "algorithm": "date_proximity", "tolerance": "2", "min_score": 0.80, "fields": { ... } }
                        ],
                        "min_score": 0.80,
                        "remarks": "Matches transactions differing by up to 2 days with identical reference and amount"
                      },
                      "rationale": "Recovers transactions with matching reference and amount that cleared within a 2-day settlement window.",
                      "evidenceIds": ["ev-1"],
                      "safetyClass": "safe_additive"
                    }
                  ]
                }
              ]
            }

            Dataset Schemas:
            {{schemaJson}}

            Existing profile rules:
            {{existingRulesJson}}

            Evidence sample:
            {{evidenceJson}}

            Context:
            {{input.Context ?? ""}}
            """;
    }

    private static OptimizationAdvisorResult ParseModelResponse(JsonElement root, OptimizationAdvisorInput input)
    {
        var result = new OptimizationAdvisorResult { Source = "model" };

        if (root.TryGetProperty("diagnoses", out var diagnosesElem) && diagnosesElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in diagnosesElem.EnumerateArray())
            {
                result.Diagnoses.Add(new DiagnosisItemDto
                {
                    Category = d.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "",
                    Summary = d.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "",
                    AffectedRecordCount = d.TryGetProperty("affectedRecordCount", out var a) && a.TryGetInt32(out var count) ? count : 0,
                    SampleEvidenceIds = d.TryGetProperty("sampleEvidenceIds", out var ids) && ids.ValueKind == JsonValueKind.Array
                        ? ids.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.ToString()).Where(x => !string.IsNullOrEmpty(x)).ToList()
                        : [],
                });
            }
        }

        if (root.TryGetProperty("change_groups", out var groupsElem) && groupsElem.ValueKind == JsonValueKind.Array)
        {
            var existingRuleIds = input.BaseProfile.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var g in groupsElem.EnumerateArray())
            {
                var groupId = g.TryGetProperty("groupId", out var gid) && gid.ValueKind == JsonValueKind.String ? gid.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                var title = g.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "Proposed improvement" : "Proposed improvement";
                var groupSafety = g.TryGetProperty("safetyClass", out var sc) && sc.ValueKind == JsonValueKind.String ? sc.GetString() ?? "safe_additive" : "safe_additive";

                var group = new ProposedChangeGroup
                {
                    GroupId = groupId,
                    Title = title,
                    SafetyClass = groupSafety,
                };

                if (g.TryGetProperty("operations", out var opsElem) && opsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var op in opsElem.EnumerateArray())
                    {
                        var changeType = op.TryGetProperty("changeType", out var ct) && ct.ValueKind == JsonValueKind.String ? ct.GetString() ?? "profile" : "profile";
                        var targetId = op.TryGetProperty("targetIdentifier", out var ti) && ti.ValueKind == JsonValueKind.String ? ti.GetString() ?? "" : "";
                        var opType = op.TryGetProperty("operationType", out var ot) && ot.ValueKind == JsonValueKind.String ? ot.GetString() ?? "append_rule" : "append_rule";

                        string opJson = "{}";
                        if (op.TryGetProperty("operationJson", out var oj))
                        {
                            opJson = oj.ValueKind == JsonValueKind.String ? (oj.GetString() ?? "{}") : oj.GetRawText();
                        }

                        var rationale = op.TryGetProperty("rationale", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
                        var safety = op.TryGetProperty("safetyClass", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "safe_additive" : "safe_additive";

                        // Validate profile rule addition
                        if (opType == "append_rule")
                        {
                            try
                            {
                                var rule = JsonSerializer.Deserialize<RuleDefinition>(opJson, ProfileJson.Options);
                                if (rule == null || string.IsNullOrWhiteSpace(rule.Id) || existingRuleIds.Contains(rule.Id))
                                {
                                    continue;
                                }
                                if (string.IsNullOrEmpty(targetId))
                                {
                                    targetId = rule.Id;
                                }
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        group.Operations.Add(new ProposedOperation
                        {
                            OperationId = Guid.NewGuid().ToString("N"),
                            DependencyGroupId = groupId,
                            ChangeType = changeType,
                            TargetIdentifier = targetId,
                            OperationType = opType,
                            OperationJson = opJson,
                            BeforeJson = "{}",
                            AfterJson = opJson,
                            Rationale = rationale,
                            SafetyClass = safety,
                            IsSelectable = true,
                        });
                    }
                }

                if (group.Operations.Count > 0)
                {
                    result.ChangeGroups.Add(group);
                }
            }
        }

        return result;
    }

    public static OptimizationAdvisorResult GenerateDeterministicAdvice(OptimizationAdvisorInput input)
    {
        var result = new OptimizationAdvisorResult
        {
            Source = "deterministic",
            Diagnoses = new List<DiagnosisItemDto>(input.Manifest.InitialDiagnoses),
        };

        var codes = input.Rows.Keys.OrderBy(c => c).ToList();
        if (codes.Count < 2) return result;

        var datasets = input.Rows;
        var existingRuleIds = input.BaseProfile.Rules.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Extract known key fields from existing exact rules in the profile
        var existingExact = input.BaseProfile.Rules.FirstOrDefault(r => r.Type == "exact" && r.Datasets.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(codes));

        var refFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var amtFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dtFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scopeCompanyFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scopeBranchFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (existingExact?.Keys != null)
        {
            foreach (var key in existingExact.Keys)
            {
                if (key.Type == "text" && key.Fields != null)
                {
                    var firstVal = key.Fields.Values.FirstOrDefault() ?? "";
                    if (firstVal.Contains("company", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var (ds, col) in key.Fields) scopeCompanyFields[ds] = col;
                    }
                    else if (firstVal.Contains("branch", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var (ds, col) in key.Fields) scopeBranchFields[ds] = col;
                    }
                    else if (refFields.Count == 0)
                    {
                        foreach (var (ds, col) in key.Fields) refFields[ds] = col;
                    }
                }
                else if (key.Type == "amount" && key.Fields != null && amtFields.Count == 0)
                {
                    foreach (var (ds, col) in key.Fields) amtFields[ds] = col;
                }
                else if (key.Type == "date" && key.Fields != null && dtFields.Count == 0)
                {
                    foreach (var (ds, col) in key.Fields) dtFields[ds] = col;
                }
            }
        }

        // 2. Fallback to column discovery for any missing fields
        foreach (var code in codes)
        {
            var cols = datasets[code].Count > 0 ? datasets[code][0].Keys.ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
            if (!refFields.ContainsKey(code))
            {
                var match = FindMatchingColumn(cols, "reference", "ref", "order_id", "transaction_id", "doc_no", "document_no", "belnr", "ticket", "invoice", "inv", "check", "chk", "pan");
                if (!string.IsNullOrEmpty(match)) refFields[code] = match;
            }
            if (!amtFields.ContainsKey(code))
            {
                var match = FindMatchingColumn(cols, "amount", "total", "net_amount", "gross_amount", "charge_amount", "wrbtr", "dmbtr", "val");
                if (!string.IsNullOrEmpty(match)) amtFields[code] = match;
            }
            if (!dtFields.ContainsKey(code))
            {
                var match = FindMatchingColumn(cols, "posting_date", "value_date", "statement_date", "tx_date", "transaction_date", "date", "bldat", "budat");
                if (!string.IsNullOrEmpty(match)) dtFields[code] = match;
            }
            if (!scopeCompanyFields.ContainsKey(code))
            {
                var match = FindMatchingColumn(cols, "company_code", "company");
                if (!string.IsNullOrEmpty(match)) scopeCompanyFields[code] = match;
            }
            if (!scopeBranchFields.ContainsKey(code))
            {
                var match = FindMatchingColumn(cols, "branch_code", "branch");
                if (!string.IsNullOrEmpty(match)) scopeBranchFields[code] = match;
            }
        }

        var hasAllRef = codes.All(refFields.ContainsKey);
        var hasAllAmt = codes.All(amtFields.ContainsKey);
        var hasAllDt = codes.All(dtFields.ContainsKey);
        var hasAllCompany = codes.All(scopeCompanyFields.ContainsKey);
        var hasAllBranch = codes.All(scopeBranchFields.ContainsKey);

        var groupCounter = 1;

        // Pattern 1: Date tolerance (identical reference and amount, +/- 2 days date proximity)
        if (hasAllRef && hasAllAmt && hasAllDt)
        {
            var ruleId = GetUniqueRuleId("PARTIAL_DATE_TOL", existingRuleIds);
            var keys = new List<KeyDefinition>();

            if (hasAllCompany)
            {
                keys.Add(new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new(scopeCompanyFields) });
            }
            if (hasAllBranch)
            {
                keys.Add(new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new(scopeBranchFields) });
            }

            keys.Add(new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new(refFields) });
            keys.Add(new KeyDefinition { Type = "amount", MatchMode = "exact", Fields = new(amtFields) });
            keys.Add(new KeyDefinition
            {
                Type = "date",
                MatchMode = "fuzzy",
                Algorithm = "date_proximity",
                Tolerance = "2",
                MinScore = 0.8m,
                Fields = new(dtFields)
            });

            var rule = new RuleDefinition
            {
                Id = ruleId,
                Type = "partial",
                Datasets = new List<string>(codes),
                Keys = keys,
                MinScore = 0.8m,
                Remarks = "Matched on reference and amount with up to 2 days settlement date lag"
            };

            var ruleJson = JsonSerializer.Serialize(rule, ProfileJson.Options);
            var groupId = $"group-{groupCounter++}";

            result.ChangeGroups.Add(new ProposedChangeGroup
            {
                GroupId = groupId,
                Title = "Add date tolerance pass (+/- 2 days)",
                SafetyClass = "safe_additive",
                Operations = [
                    new ProposedOperation
                    {
                        OperationId = Guid.NewGuid().ToString("N"),
                        DependencyGroupId = groupId,
                        ChangeType = "profile",
                        TargetIdentifier = ruleId,
                        OperationType = "append_rule",
                        OperationJson = ruleJson,
                        BeforeJson = "{}",
                        AfterJson = ruleJson,
                        Rationale = "Recovers transactions with matching reference and amount that cleared within a 2-day settlement window.",
                        SafetyClass = "safe_additive",
                        IsSelectable = true,
                    }
                ]
            });
        }

        // Pattern 2: Amount tolerance (+/- 1.00 fee / roundoff variance, identical reference and date)
        if (hasAllRef && hasAllAmt && hasAllDt)
        {
            var ruleId = GetUniqueRuleId("PARTIAL_AMT_TOL", existingRuleIds);
            var keys = new List<KeyDefinition>();

            if (hasAllCompany)
            {
                keys.Add(new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new(scopeCompanyFields) });
            }
            if (hasAllBranch)
            {
                keys.Add(new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new(scopeBranchFields) });
            }

            keys.Add(new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new(refFields) });
            keys.Add(new KeyDefinition { Type = "date", MatchMode = "exact", Fields = new(dtFields) });
            keys.Add(new KeyDefinition
            {
                Type = "amount",
                MatchMode = "fuzzy",
                Algorithm = "numeric_best_practice",
                Tolerance = "1.00",
                MinScore = 0.8m,
                Fields = new(amtFields)
            });

            var rule = new RuleDefinition
            {
                Id = ruleId,
                Type = "partial",
                Datasets = new List<string>(codes),
                Keys = keys,
                MinScore = 0.8m,
                Remarks = "Matched on reference and date with fee/penny variance up to 1.00"
            };

            var ruleJson = JsonSerializer.Serialize(rule, ProfileJson.Options);
            var groupId = $"group-{groupCounter++}";

            result.ChangeGroups.Add(new ProposedChangeGroup
            {
                GroupId = groupId,
                Title = "Add fee / small amount tolerance pass (+/- 1.00)",
                SafetyClass = "safe_additive",
                Operations = [
                    new ProposedOperation
                    {
                        OperationId = Guid.NewGuid().ToString("N"),
                        DependencyGroupId = groupId,
                        ChangeType = "profile",
                        TargetIdentifier = ruleId,
                        OperationType = "append_rule",
                        OperationJson = ruleJson,
                        BeforeJson = "{}",
                        AfterJson = ruleJson,
                        Rationale = "Recovers transactions with matching reference and date that have small fee deductions or rounding variances up to 1.00.",
                        SafetyClass = "safe_additive",
                        IsSelectable = true,
                    }
                ]
            });
        }

        // Pattern 3: Fuzzy reference pass (identical amount and date, fuzzy reference min_score 0.80)
        if (hasAllRef && hasAllAmt && hasAllDt)
        {
            var ruleId = GetUniqueRuleId("PARTIAL_FUZZY_REF", existingRuleIds);
            var keys = new List<KeyDefinition>();

            if (hasAllCompany)
            {
                keys.Add(new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new(scopeCompanyFields) });
            }
            if (hasAllBranch)
            {
                keys.Add(new KeyDefinition { Type = "text", MatchMode = "exact", Fields = new(scopeBranchFields) });
            }

            keys.Add(new KeyDefinition { Type = "amount", MatchMode = "exact", Fields = new(amtFields) });
            keys.Add(new KeyDefinition { Type = "date", MatchMode = "exact", Fields = new(dtFields) });
            keys.Add(new KeyDefinition
            {
                Type = "text",
                MatchMode = "fuzzy",
                Algorithm = "damerau_levenshtein",
                MinScore = 0.80m,
                Fields = new(refFields)
            });

            var rule = new RuleDefinition
            {
                Id = ruleId,
                Type = "partial",
                Datasets = new List<string>(codes),
                Keys = keys,
                MinScore = 0.80m,
                Remarks = "Matched on exact amount and date with typographical variance on reference"
            };

            var ruleJson = JsonSerializer.Serialize(rule, ProfileJson.Options);
            var groupId = $"group-{groupCounter++}";

            result.ChangeGroups.Add(new ProposedChangeGroup
            {
                GroupId = groupId,
                Title = "Add fuzzy reference matching pass (min score 0.80)",
                SafetyClass = "safe_additive",
                Operations = [
                    new ProposedOperation
                    {
                        OperationId = Guid.NewGuid().ToString("N"),
                        DependencyGroupId = groupId,
                        ChangeType = "profile",
                        TargetIdentifier = ruleId,
                        OperationType = "append_rule",
                        OperationJson = ruleJson,
                        BeforeJson = "{}",
                        AfterJson = ruleJson,
                        Rationale = "Recovers transactions with identical amount and date that suffered minor typographical errors in reference codes.",
                        SafetyClass = "safe_additive",
                        IsSelectable = true,
                    }
                ]
            });
        }

        return result;
    }

    private static string FindMatchingColumn(HashSet<string> columns, params string[] candidates)
    {
        foreach (var cand in candidates)
        {
            var match = columns.FirstOrDefault(c => c.Equals(cand, StringComparison.OrdinalIgnoreCase) || c.Contains(cand, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match)) return match;
        }
        return "";
    }

    private static string GetUniqueRuleId(string baseName, HashSet<string> existing)
    {
        var candidate = baseName;
        var counter = 1;
        while (existing.Contains(candidate))
        {
            candidate = $"{baseName}_{counter++}";
        }
        existing.Add(candidate);
        return candidate;
    }
}
