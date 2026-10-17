using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Grid.Reconciliation;

[Authorize]
public class AssistantAppService : ApplicationService, IAssistantAppService
{
    private const string RuleFormat = """
        You MUST output one JSON object with these fields:
        {
          "name": "Omnichannel POS and Gateway Cascade Reconciliation",
          "datasets": ["POS", "GATEWAY"],
          "rules": [
            {
              "id": "EXACT_01_FULL",
              "type": "exact",
              "datasets": ["POS", "GATEWAY"],
              "keys": [
                {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-", "REF/"]}},
                {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}},
                {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}},
                {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}},
                {"type": "text", "fields": {"POS": "terminal_id", "GATEWAY": "pos_terminal"}}
              ]
            },
            {
              "id": "PARTIAL_02_REF_FUZZY",
              "type": "partial",
              "datasets": ["POS", "GATEWAY"],
              "min_score": 0.80,
              "remarks": "Possible match on card, amount, date & terminal; reference/approval code has minor typo or OCR difference",
              "keys": [
                {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "exact"},
                {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "terminal_id", "GATEWAY": "pos_terminal"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-", "REF/"]}, "match_mode": "fuzzy", "algorithm": "damerau_levenshtein", "min_score": 0.80}
              ]
            },
            {
              "id": "PARTIAL_03_REF_DIVERGENT",
              "type": "partial",
              "datasets": ["POS", "GATEWAY"],
              "min_score": 0.80,
              "remarks": "Possible match on card, amount, date & terminal; approval/reference code divergent",
              "keys": [
                {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "exact"},
                {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "terminal_id", "GATEWAY": "pos_terminal"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-", "REF/"]}, "match_mode": "divergent", "min_score": 0.80}
              ]
            },
            {
              "id": "PARTIAL_04_CARD_DIVERGENT",
              "type": "partial",
              "datasets": ["POS", "GATEWAY"],
              "min_score": 0.80,
              "remarks": "Possible match on ref, amount, date & terminal; card number divergent",
              "keys": [
                {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-", "REF/"]}, "match_mode": "exact"},
                {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "exact"},
                {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "terminal_id", "GATEWAY": "pos_terminal"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "divergent", "min_score": 0.80}
              ]
            },
            {
              "id": "PARTIAL_05_DATE_LAG",
              "type": "partial",
              "datasets": ["POS", "GATEWAY"],
              "min_score": 0.80,
              "remarks": "Possible match with 1-3 day date settlement difference",
              "keys": [
                {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-", "REF/"]}, "match_mode": "exact"},
                {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "exact"},
                {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "fuzzy", "algorithm": "date_proximity", "min_score": 0.80}
              ]
            },
            {
              "id": "PARTIAL_06_AMOUNT_VARIANCE",
              "type": "partial",
              "datasets": ["POS", "GATEWAY"],
              "min_score": 0.80,
              "remarks": "Possible match on ref & date; amount variance within tolerance",
              "keys": [
                {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-", "REF/"]}, "match_mode": "exact"},
                {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "exact"},
                {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "fuzzy", "algorithm": "numeric_best_practice", "tolerance": "1.00", "min_score": 0.80}
              ]
            },
            {
              "id": "PARTIAL_07_COMBINED_VARIANCE",
              "type": "partial",
              "datasets": ["POS", "GATEWAY"],
              "min_score": 0.80,
              "remarks": "Possible match on ref & amount; card divergent and date settlement difference",
              "keys": [
                {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-", "REF/"]}, "match_mode": "exact"},
                {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "exact"},
                {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "divergent", "min_score": 0.80},
                {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "fuzzy", "algorithm": "date_proximity", "min_score": 0.80}
              ]
            }
          ]
        }
        Supported rule types:
        1. "exact": Uniquely matches ONE row from EVERY participating dataset on ALL normalized keys.
        2. "aggregate": Matches 1 target row to 1+ source rows grouped by keys where source sum equals target amount within tolerance. Also supply "target", "source", "target_amount", "source_amount", and string "tolerance".
        3. "partial": Multi-pass rule where a subset of keys match exactly (anchors) and remaining keys allow fuzzy matching, tolerances, or expected mismatch/divergence.
           - Anchor exact keys: Use "match_mode":"exact" (or omit match_mode). At least one key (preferably 2 or 3) MUST be exact to partition candidates deterministically.
           - Text keys: Use "match_mode":"fuzzy", "algorithm":"damerau_levenshtein", and "min_score" (always 0.80 to match single-character typos on 6-character auth codes).
           - Amount keys: Use "match_mode":"fuzzy", "algorithm":"numeric_best_practice", optional "tolerance" (e.g. "1.00"), and "min_score" (e.g. 0.80).
           - Date keys: Use "match_mode":"fuzzy", "algorithm":"date_proximity", and "min_score" (e.g. 0.80 for 1-2 day settlement lag).
           - Divergent keys (expected mismatch): Use "match_mode":"divergent" or "expected_mismatch":true so it flags a recon remark like "Possible match but {key} is not matched".
        Use only supplied dataset codes, existing column names, and rule types. Each key must contain "type" (text, amount, date) and "fields" mapping EVERY rule dataset code to its corresponding column name. "strip_prefixes" is optional.
        """;

    private readonly IConfiguration _configuration;
    private readonly ReconciliationData _data;
    private readonly IRepository<ReconciliationRun, Guid> _runs;

    public AssistantAppService(
        IConfiguration configuration,
        ReconciliationData data,
        IRepository<ReconciliationRun, Guid> runs)
    {
        _configuration = configuration;
        _data = data;
        _runs = runs;
    }

    public async Task<ProposalDto> ProposeAsync(ProposeProfileDto input)
    {
        var codes = input.DatasetCodes ?? [];
        if (codes.Count is < 2 or > 3 || codes.Distinct(StringComparer.Ordinal).Count() != codes.Count)
            throw new UserFriendlyException("Select two or three datasets.");
        Dictionary<string, List<Dictionary<string, string>>> rows;
        try
        {
            rows = input.CsvSamples is { Count: > 0 }
                ? codes.ToDictionary(code => code, code => ReconciliationEngine.ReadCsv(input.CsvSamples.TryGetValue(code, out var csv) ? csv : ""), StringComparer.Ordinal)
                : await _data.LoadAsync(codes, input.ParserRunIds);
        }
        catch (ReconciliationException ex)
        {
            throw new UserFriendlyException(ex.Message);
        }
        var prompt = """
            You are an expert financial reconciliation engineer. Design an in-depth, deterministic multi-pass reconciliation profile for the provided datasets.

            CRITICAL DESIGN REQUIREMENTS:
            1. BROAD KEY DISCOVERY (DO NOT LIMIT TO 2 OR 3 KEYS):
               Inspect all CSV column headers and sample data carefully. Identify ALL corresponding semantic fields across the datasets (aim for 4 to 8 keys when candidate columns exist). Look for:
               - Transaction Reference / Approval Code / ID / Order / Receipt / Invoice
               - Financial Amount / Gross / Paid / Deposit / Net
               - Date / Time / Timestamp / Settlement Date
               - Payment Card / Masked PAN / Card Last 4 / Account Number
               - Terminal ID / Store ID / Branch Code / Channel / Register
               - Customer / Payer Name / Customer ID
               - Authorization Code / Approval Code / RRN / Trace Number
               Map all discovered semantic columns into the keys of your rules!

            2. CRITICAL MIN_SCORE FOR 6-CHARACTER CODES (e.g. APPROVAL/AUTH CODES):
               In retail and banking, authorization/approval codes are standard 6 characters long (e.g. '036551', '546357').
               A single-character typo, transposition, or OCR misread (such as 'I' vs '1', 'O' vs '0', 'B' vs '8', 'S' vs '5') has an edit similarity of exactly 1 - (1/6) = 0.8333.
               YOU MUST USE min_score: 0.80 (or 0.75) for fuzzy reference/code matching!
               NEVER set min_score to 0.85 or higher for reference matching, as that will falsely reject standard 6-character codes with a single typo!

            3. DUAL-ANCHOR PERSPECTIVES (BOTH REF-ANCHORED AND CARD-ANCHORED PASSES):
               Discrepancies in transactional data happen in two complementary directions:
               - Perspective A (Card Divergent, Ref Anchor): The transaction reference matches, but the card number diverges (mobile wallet token, virtual card, masked PAN). Anchor on Ref + Amount + Date + Branch; mark Card Number as 'divergent'.
               - Perspective B (Reference Divergent, Card Anchor): The 16-digit Card Number, Amount, Date, and Branch match, but the approval/reference code diverges (e.g. POS terminal sequence/invoice number vs bank host approval code, or OCR typo like '03655I' vs '036551', or 'APPRVD' vs 'APPROV'). Anchor on Card Number + Amount + Date + Branch; mark Reference Code as 'fuzzy' (min_score: 0.80) or 'divergent'!
               You MUST include BOTH perspectives in your cascade!

            4. IN-DEPTH MULTI-PASS WATERFALL CASCADE (GENERATE 4 TO 7 SEQUENTIAL PASSES):
               Reconciliation rules execute sequentially in a waterfall cascade. Once records match in an earlier pass, they are permanently resolved and excluded from subsequent passes.
               Do NOT stop after 1 or 2 shallow passes. You MUST construct a comprehensive multi-pass cascade:
               - Pass 1 (Perfect Full Exact Match): All candidate keys exact (e.g. Ref + Amount + Date + Card + Branch/Terminal).
               - Pass 2 (Reference Typo / OCR Swap with Card Anchor): Anchor on Card + Amount + Date + Branch; Reference fuzzy (algorithm: "damerau_levenshtein", min_score: 0.80). Remarks: "Possible match on card, amount, date & branch; approval/reference code has minor typo or OCR difference".
               - Pass 3 (Reference Divergent with Card Anchor): Anchor on Card + Amount + Date + Branch; Reference divergent ("match_mode":"divergent", min_score: 0.80). Remarks: "Possible match on card, amount, date & branch; approval/reference code divergent".
               - Pass 4 (Card Divergent with Reference Anchor): Anchor on Ref + Amount + Date + Branch; Card divergent ("match_mode":"divergent", min_score: 0.80). Remarks: "Possible match on reference, amount, date & branch; card number divergent".
               - Pass 5 (Settlement Date Lag): Anchor on Ref + Amount + Card + Branch; Date proximity ("match_mode":"fuzzy", "algorithm":"date_proximity", min_score: 0.80). Remarks: "Possible match with 1-3 day date settlement difference".
               - Pass 6 (Amount / Fee Variance): Anchor on Ref + Date + Card + Branch; Amount tolerance ("match_mode":"fuzzy", "algorithm":"numeric_best_practice", tolerance: "1.00", min_score: 0.80). Remarks: "Possible match on ref, date & card; amount variance within tolerance".
               - Pass 7 (Compound Variance with Strong Core Anchors): When core anchors (Ref + Amount or Card + Amount) match, allow compound divergence (e.g. Card divergent AND Date proximity lag) with min_score: 0.80. Remarks: "Possible match on core attributes; secondary attributes have minor divergence".

            5. DETERMINISTIC ANCHORS:
               Every partial rule MUST specify at least 1 or 2 anchor keys with "match_mode":"exact" so candidates are indexed and partitioned deterministically.

            6. STRICT ADHERENCE:
               Use only supplied dataset codes and existing column names. Output ONLY the JSON object. No markdown text outside the JSON.
            """
            + "\n" + RuleFormat
            + "\nBusiness context: " + TrimContext(input.Context)
            + "\nCSV profiles:\n" + JsonSerializer.Serialize(Sample(rows, 3));
        try
        {
            var drafted = await CallGeminiAsync(prompt);
            var profile = ProfileJson.Parse(drafted.GetRawText());
            ReconciliationEngine.ValidateProfile(profile, rows);
            profile.Version = 1;
            return new ProposalDto { DefinitionJson = ProfileJson.Write(profile) };
        }
        catch
        {
            // Fallback to high-speed deterministic heuristic profile generator on timeout, error, or quota
            var heuristic = GenerateHeuristicProfile(codes, rows, input.Context);
            heuristic.Version = 1;
            return new ProposalDto { DefinitionJson = ProfileJson.Write(heuristic) };
        }
    }

    public async Task<ParsersProposalDto> ProposeParsersAsync(ProposeParsersDto input)
    {
        if (input.Files.Count == 0)
        {
            return new ParsersProposalDto();
        }

        var prompt = """
            You are an expert ETL parser architect.
            Analyze the following raw snippet(s) from unclean data file(s) to determine how each file must be parsed into a clean tabular dataset.

            For each file:
            1. DELIMITER DETECTION: Identify the single-character delimiter:
               "," for comma, "|" for pipe, "\t" for tab, ";" for semicolon.
            2. HEADER & PREAMBLE DETECTION:
               - If the file starts immediately with the column headers on line 1:
                 headerMode="firstRow", headerRowIndex=0, skipRows=0.
               - If the file has title banners, decorative borders ("===", "---"), report generation metadata, or notes before the real column header row:
                 Find the exact line number containing the column headers (0-indexed). Set headerMode="rowIndex", headerRowIndex=<index>, skipRows=<index>.
               - CRITICAL: If the file has NO COLUMN HEADERS (every line, including line 1, is raw transactional data, dates, card numbers, or amounts like bank clearing files):
                 Set headerMode="none", headerRowIndex=0, skipRows=0.
            3. TOTAL & TRAILER ROWS:
               - If the file ends with footer summary rows, counts, batches, totals, or end-of-file lines (e.g. "TOTAL COUNT", "TOTAL AMOUNT", "*** END OF REPORT ***", "BATCH TOTAL", "GRAND TOTAL"):
                 Set removeTotalRows=true and totalRowKeywords="TOTAL,COUNT,SUM,***,BATCH". Otherwise removeTotalRows=false and totalRowKeywords="".
            4. TRANSFORMATION STEPS (clean dirty columns to canonical form):
               Generate an ordered list of steps using ONLY these step kinds:
               - "parseDate": When a date column is in non-ISO format (e.g. DD/MM/YYYY, MM/DD/YYYY, YYYYMMDD, or DDMYYYY).
                 params: {"format": "%d/%m/%Y" or "%m/%d/%Y" or "%Y%m%d" or "%m%d%Y" or "%d%m%Y"}
               - "scaleCents": When a currency / amount column is expressed in integer cents without a decimal point (e.g. 86900 for 869.00, or 0001288215 for 12882.15).
                 params: {"scaleFactor": "0.01"}
                 CRITICAL: If a file contains amounts in integer centavos (which are 100x larger than standard decimal amounts in POS/counterpart files), you MUST apply scaleCents to normalize to decimal units.
               - "replace": When an amount or text column has currency symbols ("$", "PHP", "USD") or commas (",") that need to be removed.
                 params: {"pattern": ",", "replacement": ""} or {"pattern": "$", "replacement": ""}
               - "trimChars": When an account, invoice, terminal, or authorization code has leading zeroes that should be stripped (e.g. "0006" -> "6").
                 params: {"characters": "0", "side": "start"}
               - "maskCard": When a credit/debit card number is unmasked (15 to 19 digits).
                 params: {"keepStart": "6", "keepEnd": "4", "maskCharacter": "*"}
               - "trim": To strip leading/trailing whitespace.
                 params: {}

            5. CRITICAL FOR HEADERLESS FILES (headerMode="none", columns named column_1, column_2, ...):
               You MUST still generate transformation steps for headerless files:
               - 8-digit compact date (e.g. 08122026) -> "parseDate" with format "%m%d%Y".
               - 16-19 digit card PAN (e.g. 0005125710000008999) -> "maskCard".
               - Integer centavos amounts (e.g. 0001288215) -> "scaleCents" with scaleFactor "0.01".

            Output a single JSON object with this EXACT structure:
            {
              "parsers": [
                {
                  "code": "DATASET_CODE",
                  "delimiter": ",",
                  "headerMode": "firstRow",
                  "headerRowIndex": 0,
                  "skipRows": 0,
                  "removeTotalRows": false,
                  "totalRowKeywords": "",
                  "steps": [
                    {
                      "id": "step-1",
                      "kind": "parseDate",
                      "enabled": true,
                      "label": "Parse transaction date",
                      "source": "raw_date_column_name",
                      "params": {"format": "%d%m%Y"}
                    }
                  ]
                }
              ]
            }
            """
            + "\nBusiness context: " + TrimContext(input.Context)
            + "\nFiles to analyze:\n" + JsonSerializer.Serialize(input.Files.Select(f => new
            {
                code = f.Code,
                fileName = f.FileName,
                snippet = string.Join("\n", (f.RawSnippet ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(8)).Length > 800
                    ? string.Join("\n", (f.RawSnippet ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(8))[..800]
                    : string.Join("\n", (f.RawSnippet ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(8))
            }));

        if (input.Files.Count > 3)
        {
            return FallbackAnalyzeParsers(input.Files);
        }

        try
        {
            var drafted = await CallGeminiAsync(prompt);
            var result = ParseParserProposalJson(drafted);
            if (result.Parsers.Count > 0)
            {
                return result;
            }
        }
        catch
        {
            // Fallback to deterministic heuristic analyzer if Gemini is unavailable
        }

        return FallbackAnalyzeParsers(input.Files);
    }

    public async Task<InvestigationDto> InvestigateAsync(InvestigateInput input)
    {
        var run = await _runs.GetAsync(input.RunId);
        var rows = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(run.DatasetSnapshotJson)
            ?? throw new UserFriendlyException("This run has no dataset snapshot.");
        var profile = ProfileJson.Parse(run.DefinitionSnapshotJson);
        var result = ReconciliationEngine.Reconcile(rows, profile);
        var remaining = result.Unresolved
            .GroupBy(row => row.Dataset)
            .ToDictionary(group => group.Key, group => group.Take(12).ToList());
        var prompt = """
            Analyze why these records remained unmatched using the EXISTING configuration.
            Synthesize an improved, in-depth multi-pass waterfall profile covering combinations of potential matching key misses, secondary key divergences, settlement date lags, fee variances, and OCR typos on 6-character codes (using min_score: 0.80).
            Ensure both Card-anchored (Ref divergent/fuzzy) and Ref-anchored (Card divergent) perspectives are included.
            Output a JSON object with {"diagnosis":string, "hypotheses":[string], "proposed_profile": profile object, "change_summary":[string], "risks":[string]}.
            Ensure proposed_profile has an in-depth multi-pass waterfall cascade (4 to 7 passes) with strong anchor keys in partial rules and meaningful remarks.
            """
            + "\n" + RuleFormat
            + "\nContext: " + TrimContext(input.Context)
            + "\nExisting configuration: " + run.DefinitionSnapshotJson
            + "\nCSV schema/samples: " + JsonSerializer.Serialize(Sample(rows, 5))
            + "\nUnresolved sampled by dataset: " + JsonSerializer.Serialize(remaining)
            + "\nPrevious run stats: " + JsonSerializer.Serialize(result.Statistics);
        var drafted = await CallGeminiAsync(prompt);
        if (!drafted.TryGetProperty("proposed_profile", out var proposedElement) || proposedElement.ValueKind != JsonValueKind.Object)
        {
            throw new UserFriendlyException("AI response must include proposed_profile");
        }

        ProfileDefinition proposed;
        try
        {
            proposed = ProfileJson.Parse(proposedElement.GetRawText());
            ReconciliationEngine.ValidateProfile(proposed, rows);
        }
        catch (ReconciliationException ex)
        {
            throw new UserFriendlyException("The proposed profile did not pass validation: " + ex.Message);
        }

        return new InvestigationDto
        {
            Diagnosis = ReadString(drafted, "diagnosis"),
            Hypotheses = ReadStrings(drafted, "hypotheses"),
            ProposedDefinitionJson = ProfileJson.Write(proposed),
            ChangeSummary = ReadStrings(drafted, "change_summary"),
            Risks = ReadStrings(drafted, "risks"),
        };
    }

    public async Task<RegressionPreviewDto> PreviewRegressionAsync(RegressionPreviewInput input)
    {
        ProfileDefinition proposed;
        try
        {
            proposed = ProfileJson.Parse(input.ProposedDefinitionJson);
        }
        catch (ReconciliationException ex)
        {
            throw new UserFriendlyException(ex.Message);
        }

        var preview = new RegressionPreviewDto();
        ReconciliationRun? run = null;
        if (input.RunId.HasValue)
        {
            run = await _runs.GetAsync(input.RunId.Value);
        }

        try
        {
            var currentRows = run == null
                ? await _data.LoadAsync(proposed.Datasets)
                : JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(run.DatasetSnapshotJson) ?? [];
            var currentDefinition = run == null
                ? proposed
                : ProfileJson.Parse(run.DefinitionSnapshotJson);
            if (run != null)
            {
                preview.CurrentProfile = ToStats(ReconciliationEngine.Reconcile(currentRows, currentDefinition).Statistics);
            }

            preview.ProposedOnCurrent = ToStats(ReconciliationEngine.Reconcile(currentRows, proposed).Statistics);

            var baseline = await GetBaselineAsync(run?.PackageId, input.ProfileId);
            preview.HasBaseline = baseline != null;
            if (baseline == null)
            {
                preview.Passed = true;
                preview.Message = "No original batch is stored yet. The first successful run creates the regression baseline.";
                return preview;
            }

            var baselineRows = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(baseline.DatasetsJson) ?? [];
            var baselineProfile = ProfileJson.Parse(baseline.ProfileJson);
            var previous = ReconciliationEngine.Reconcile(baselineRows, baselineProfile);
            var next = ReconciliationEngine.Reconcile(baselineRows, proposed);
            preview.BaselineProfile = ToStats(previous.Statistics);
            preview.ProposedOnBaseline = ToStats(next.Statistics);
            preview.Passed = ReconciliationEngine.PreservesMatches(previous, next);
            preview.Message = preview.Passed
                ? "Previously matched groups on the original batch are still matched. Review the new matches before approving."
                : "Regression check failed: previously matched groups changed. Revision cannot be approved.";
            return preview;
        }
        catch (ReconciliationException ex)
        {
            throw new UserFriendlyException(ex.Message);
        }
    }

    public async Task<bool> GetConnectionTestAsync()
    {
        var response = await CallGeminiAsync("Return exactly this JSON object: {\"connected\": true}");
        return response.TryGetProperty("connected", out var connected) && connected.ValueKind == JsonValueKind.True;
    }

    public async Task<AiFallbackDecisionDto> DecideFallbackAsync(AiFallbackRequestDto input)
    {
        var isNetworkOrTimeout = !string.IsNullOrWhiteSpace(input.ErrorMessage) &&
            (input.ErrorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
             input.ErrorMessage.Contains("could not be reached", StringComparison.OrdinalIgnoreCase) ||
             input.ErrorMessage.Contains("canceled", StringComparison.OrdinalIgnoreCase));

        if (isNetworkOrTimeout)
        {
            return new AiFallbackDecisionDto
            {
                CanRecover = true,
                Diagnosis = "External AI request timed out under heavy multi-file load. Switched to high-speed deterministic heuristic engine to continue configuration without burning further credits.",
                ActionTaken = "switch_to_fast_heuristic",
                UserMessage = "AI Decision: Gemini generation timed out under heavy multi-file load. Switched to high-speed deterministic heuristic engine to continue configuration without burning further credits.",
                FilesToSkip = [],
                AdjustedParsers = []
            };
        }

        var filesToSkip = new List<string>();
        var nonCorruptFiles = new List<FileAnomalyDto>();

        foreach (var file in input.AnomalousFiles)
        {
            var snippet = (file.RawSnippet ?? "").Trim();
            // A file is truly empty ONLY if FileSize is 0, or if FileSize is small (<50 bytes) and snippet is empty/whitespace
            var isTrulyEmpty = file.FileSize == 0 || (file.FileSize < 50 && snippet.Length == 0);
            if (isTrulyEmpty)
            {
                filesToSkip.Add(file.FileName);
            }
            else
            {
                nonCorruptFiles.Add(file);
            }
        }

        if (filesToSkip.Count > 0 && nonCorruptFiles.Count == 0)
        {
            return new AiFallbackDecisionDto
            {
                CanRecover = true,
                Diagnosis = $"Detected {filesToSkip.Count} empty or zero-byte file(s) containing no transactional records ({string.Join(", ", filesToSkip)}).",
                ActionTaken = "skip_empty_files",
                UserMessage = $"AI Decision: Automatically bypassed empty file(s) ({string.Join(", ", filesToSkip)}) that contain no data records. Reconciliation proceeds with valid transactional files.",
                FilesToSkip = filesToSkip
            };
        }

        var prompt = """
            You are an autonomous AI reconciliation engineer and ETL architect.
            An anomaly or error occurred during pipeline execution:
            """
            + $"\n- Step: {input.StepIndex} ({input.StepName})"
            + $"\n- Error Message: {input.ErrorMessage}"
            + $"\n- Business Context: {TrimContext(input.Context)}"
            + "\n- Files involved:\n" + JsonSerializer.Serialize(input.AnomalousFiles.Select(f => new
            {
                fileName = f.FileName,
                datasetCode = f.DatasetCode,
                parserCode = f.ParserCode,
                fileSize = f.FileSize,
                snippet = (f.RawSnippet ?? "").Length > 400 ? (f.RawSnippet ?? "")[..400] : (f.RawSnippet ?? ""),
                error = f.ErrorDetail
            }))
            + """

            GUIDANCE FOR DECISION MAKING:
            1. EMPTY / ZERO-BYTE FILES:
               If a file has 0 bytes, only whitespace, or no transaction rows, add its fileName to filesToSkip.
            2. HEADERLESS TRANSACTIONAL DATA:
               If a file has NO headers (every line starting from line 1 consists of transaction data, e.g. dates, card numbers, or amounts):
               Set headerMode="none", headerRowIndex=0, skipRows=0.
               Identify the correct delimiter (comma ",", tab "\t", pipe "|", semicolon ";").
            3. DELIMITER OR PREAMBLE ANOMALIES:
               If header row is preceded by report titles/borders, set headerMode="rowIndex" and headerRowIndex to the line index of the actual headers.
            4. CORRUPTED / UNREADABLE:
               If completely unrecoverable, set canRecover=false with a clear explanation in diagnosis.

            Output ONLY a JSON object with this EXACT structure:
            {
              "canRecover": true,
              "diagnosis": "Detailed technical analysis of what was observed",
              "actionTaken": "skip_empty_files" | "adjust_parser_config" | "retry_with_none_header",
              "userMessage": "Clear, friendly explanation of how AI resolved the anomaly so reconciliation proceeds seamlessly",
              "filesToSkip": ["filename.txt"],
              "adjustedParsers": [
                {
                  "code": "PARSER_CODE",
                  "delimiter": ",",
                  "headerMode": "none",
                  "headerRowIndex": 0,
                  "skipRows": 0,
                  "removeTotalRows": false,
                  "totalRowKeywords": "",
                  "steps": []
                }
              ]
            }
            """;

        try
        {
            var drafted = await CallGeminiAsync(prompt);
            var decision = ParseFallbackDecisionJson(drafted);

            // Sanity check: Never allow non-empty files to be bypassed by hallucination
            decision.FilesToSkip = decision.FilesToSkip
                .Where(name => input.AnomalousFiles.Any(f => f.FileName == name && (f.FileSize == 0 || (f.FileSize < 50 && string.IsNullOrWhiteSpace(f.RawSnippet)))))
                .ToList();

            foreach (var skipped in filesToSkip)
            {
                if (!decision.FilesToSkip.Contains(skipped))
                {
                    decision.FilesToSkip.Add(skipped);
                }
            }

            if (!string.IsNullOrWhiteSpace(decision.Diagnosis))
            {
                return decision;
            }
        }
        catch
        {
            // Fallback to deterministic heuristic decision if Gemini is unreachable
        }

        return FallbackDecideAnomaly(input, filesToSkip, nonCorruptFiles);
    }

    private async Task<RegressionBaseline?> GetBaselineAsync(Guid? packageId, Guid profileId)
    {
        Guid resolvedPackageId;
        if (packageId.HasValue)
        {
            resolvedPackageId = packageId.Value;
        }
        else
        {
            var profiles = LazyServiceProvider.LazyGetRequiredService<IRepository<Profile, Guid>>();
            var profile = await profiles.GetAsync(profileId);
            resolvedPackageId = profile.PackageId;
        }

        var baselines = LazyServiceProvider.LazyGetRequiredService<IRepository<RegressionBaseline, Guid>>();
        return await baselines.FirstOrDefaultAsync(item => item.PackageId == resolvedPackageId);
    }

    private async Task<JsonElement> CallGeminiAsync(string prompt)
    {
        var key = FirstNonEmpty(_configuration["Gemini:ApiKey"], Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new UserFriendlyException("Assistant unavailable. Set GEMINI_API_KEY to draft a profile. Manual editing still works.");
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
            if (detail.Length > 500)
            {
                detail = detail[..500];
            }

            var hint = (int)response.StatusCode switch
            {
                404 => " Verify GEMINI_MODEL. Use gemini-3.8-flash or another model enabled for your key.",
                401 or 403 => " Verify that your API key is active and permitted to call Gemini.",
                429 => " API rate limit or quota exceeded.",
                _ => "",
            };
            throw new UserFriendlyException($"Gemini returned HTTP {(int)response.StatusCode} for model '{model}': {detail}{hint}");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var parts = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
            var raw = string.Concat(parts.EnumerateArray()
                .Where(part => !part.TryGetProperty("thought", out var thought) || thought.ValueKind != JsonValueKind.True)
                .Select(part => part.TryGetProperty("text", out var text) ? text.GetString() : ""));
            raw = StripFence(raw);
            using var parsed = JsonDocument.Parse(raw);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new UserFriendlyException("AI must return one JSON object");
            }

            return parsed.RootElement.Clone();
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new UserFriendlyException("Gemini responded, but did not return a valid JSON configuration.");
        }
    }

    private static object Sample(Dictionary<string, List<Dictionary<string, string>>> rows, int limit)
    {
        return rows.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var sampleRows = pair.Value.Take(limit)
                    .Select(r => r.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                                  .ToDictionary(kv => kv.Key, kv => kv.Value.Length > 35 ? kv.Value[..35] : kv.Value))
                    .ToList();
                var activeColumns = sampleRows.SelectMany(r => r.Keys).Distinct().ToList();
                return new
                {
                    columns = activeColumns.Count > 0 ? activeColumns : pair.Value.FirstOrDefault()?.Keys.Take(12).ToList() ?? [],
                    row_count = pair.Value.Count,
                    sample_rows = sampleRows,
                };
            });
    }

    private static ProfileDefinition GenerateHeuristicProfile(
        List<string> codes,
        Dictionary<string, List<Dictionary<string, string>>> rows,
        string? context)
    {
        var schemas = codes.ToDictionary(
            c => c,
            c => rows.TryGetValue(c, out var rList) && rList.Count > 0
                ? rList[0].Keys.ToList()
                : [],
            StringComparer.Ordinal);

        string? FindColumn(string code, string pattern)
        {
            if (!schemas.TryGetValue(code, out var cols)) return null;
            return cols.FirstOrDefault(c => System.Text.RegularExpressions.Regex.IsMatch(c, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        var refCols = new Dictionary<string, string>();
        var amtCols = new Dictionary<string, string>();
        var dateCols = new Dictionary<string, string>();
        var cardCols = new Dictionary<string, string>();
        var termCols = new Dictionary<string, string>();

        foreach (var code in codes)
        {
            refCols[code] = FindColumn(code, "(app_code|approval|auth|rrn|invoice|order_id)")
                ?? FindColumn(code, "(id|ref|order|code|trans|receipt|seq|ticket)")
                ?? schemas[code].FirstOrDefault() ?? "column_1";

            amtCols[code] = FindColumn(code, "(gross_amt|gross|amount|amt|paid|total|charge|net|fee|price)")
                ?? schemas[code].ElementAtOrDefault(1) ?? refCols[code];

            dateCols[code] = FindColumn(code, "(trx_date|trans_date|transaction_date|sale_date)")
                ?? FindColumn(code, "(date|time|timestamp|settle|sale|post)")
                ?? schemas[code].ElementAtOrDefault(2) ?? refCols[code];

            var cardCol = FindColumn(code, "(card|pan|account|acct)");
            if (cardCol != null) cardCols[code] = cardCol;
        }

        var profile = new ProfileDefinition
        {
            Name = $"{string.Join(" & ", codes)} Waterfall Cascade Reconciliation",
            Datasets = codes,
            Rules = []
        };

        // Pass 1: Exact Match
        var pass1Keys = new List<KeyDefinition>
        {
            new() { Type = "text", Fields = refCols },
            new() { Type = "amount", Fields = amtCols },
            new() { Type = "date", Fields = dateCols }
        };
        if (cardCols.Count == codes.Count) pass1Keys.Add(new() { Type = "text", Fields = cardCols });

        profile.Rules.Add(new RuleDefinition
        {
            Id = "EXACT_01_FULL",
            Type = "exact",
            Datasets = codes,
            Keys = pass1Keys
        });

        // Pass 2: Fuzzy Reference with Amount+Date Anchor (and Card if available)
        var pass2Keys = new List<KeyDefinition>
        {
            new() { Type = "amount", Fields = amtCols, MatchMode = "exact" },
            new() { Type = "date", Fields = dateCols, MatchMode = "exact" },
            new() { Type = "text", Fields = refCols, MatchMode = "fuzzy", Algorithm = "damerau_levenshtein", MinScore = 0.80m }
        };
        if (cardCols.Count == codes.Count) pass2Keys.Insert(0, new() { Type = "text", Fields = cardCols, MatchMode = "exact" });

        profile.Rules.Add(new RuleDefinition
        {
            Id = "PARTIAL_02_REF_FUZZY",
            Type = "partial",
            Datasets = codes,
            MinScore = 0.80m,
            Remarks = "Possible match on amount & date; reference code has minor typo or OCR difference",
            Keys = pass2Keys
        });

        // Pass 3: Divergent Reference with Core Anchors
        var pass3Keys = new List<KeyDefinition>
        {
            new() { Type = "amount", Fields = amtCols, MatchMode = "exact" },
            new() { Type = "date", Fields = dateCols, MatchMode = "exact" },
            new() { Type = "text", Fields = refCols, MatchMode = "divergent", MinScore = 0.80m }
        };
        if (cardCols.Count == codes.Count) pass3Keys.Insert(0, new() { Type = "text", Fields = cardCols, MatchMode = "exact" });

        profile.Rules.Add(new RuleDefinition
        {
            Id = "PARTIAL_03_REF_DIVERGENT",
            Type = "partial",
            Datasets = codes,
            MinScore = 0.80m,
            Remarks = "Possible match on core attributes; approval/reference code divergent",
            Keys = pass3Keys
        });

        // Pass 4: Settlement Date Lag
        var pass4Keys = new List<KeyDefinition>
        {
            new() { Type = "text", Fields = refCols, MatchMode = "exact" },
            new() { Type = "amount", Fields = amtCols, MatchMode = "exact" },
            new() { Type = "date", Fields = dateCols, MatchMode = "fuzzy", Algorithm = "date_proximity", MinScore = 0.80m }
        };
        if (cardCols.Count == codes.Count) pass4Keys.Add(new() { Type = "text", Fields = cardCols, MatchMode = "exact" });

        profile.Rules.Add(new RuleDefinition
        {
            Id = "PARTIAL_04_DATE_LAG",
            Type = "partial",
            Datasets = codes,
            MinScore = 0.80m,
            Remarks = "Possible match with 1-3 day date settlement difference",
            Keys = pass4Keys
        });

        // Pass 5: Amount Variance / Fee Variance
        var pass5Keys = new List<KeyDefinition>
        {
            new() { Type = "text", Fields = refCols, MatchMode = "exact" },
            new() { Type = "date", Fields = dateCols, MatchMode = "exact" },
            new() { Type = "amount", Fields = amtCols, MatchMode = "fuzzy", Algorithm = "numeric_best_practice", Tolerance = "1.00", MinScore = 0.80m }
        };
        if (cardCols.Count == codes.Count) pass5Keys.Add(new() { Type = "text", Fields = cardCols, MatchMode = "exact" });

        profile.Rules.Add(new RuleDefinition
        {
            Id = "PARTIAL_05_AMOUNT_VARIANCE",
            Type = "partial",
            Datasets = codes,
            MinScore = 0.80m,
            Remarks = "Possible match on reference & date; amount variance within tolerance",
            Keys = pass5Keys
        });

        return profile;
    }

    private static string TrimContext(string? context)
    {
        var value = context?.Trim() ?? "";
        return value.Length <= 1200 ? value : value[..1200];
    }

    private static string StripFence(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstNewline = text.IndexOf('\n');
        if (firstNewline >= 0)
        {
            text = text[(firstNewline + 1)..];
        }

        if (text.EndsWith("```", StringComparison.Ordinal))
        {
            text = text[..^3];
        }

        return text.Trim();
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static List<string> ReadStrings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .ToList();
    }

    private static StatisticsDto ToStats(ReconcileStatistics statistics)
    {
        return new StatisticsDto
        {
            TotalRecords = statistics.TotalRecords,
            MatchedRecords = statistics.MatchedRecords,
            UnresolvedRecords = statistics.UnresolvedRecords,
            AmbiguousRecords = statistics.AmbiguousRecords,
            MatchedGroups = statistics.MatchedGroups,
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static ParsersProposalDto ParseParserProposalJson(JsonElement root)
    {
        var result = new ParsersProposalDto();
        if (!root.TryGetProperty("parsers", out var parsersElement) || parsersElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in parsersElement.EnumerateArray())
        {
            var parserItem = ParseParserItem(item);
            if (!string.IsNullOrEmpty(parserItem.Code))
            {
                result.Parsers.Add(parserItem);
            }
        }

        return result;
    }

    private static ParserProposalItemDto ParseParserItem(JsonElement item)
    {
        var code = ReadString(item, "code");
        var delimiter = ReadString(item, "delimiter");
        if (string.IsNullOrEmpty(delimiter)) delimiter = ",";

        var headerMode = ReadString(item, "headerMode");
        if (string.IsNullOrEmpty(headerMode)) headerMode = ReadString(item, "header_mode");
        if (headerMode != "rowIndex" && headerMode != "firstRow" && headerMode != "none") headerMode = "firstRow";

        var headerRowIndex = 0;
        if (item.TryGetProperty("headerRowIndex", out var hVal) && hVal.TryGetInt32(out var hIdx)) headerRowIndex = hIdx;
        else if (item.TryGetProperty("header_row_index", out var hVal2) && hVal2.TryGetInt32(out var hIdx2)) headerRowIndex = hIdx2;

        var skipRows = 0;
        if (item.TryGetProperty("skipRows", out var sVal) && sVal.TryGetInt32(out var sIdx)) skipRows = sIdx;
        else if (item.TryGetProperty("skip_rows", out var sVal2) && sVal2.TryGetInt32(out var sIdx2)) skipRows = sIdx2;

        var removeTotal = (item.TryGetProperty("removeTotalRows", out var rVal) && rVal.ValueKind == JsonValueKind.True) ||
                          (item.TryGetProperty("remove_total_rows", out var rVal2) && rVal2.ValueKind == JsonValueKind.True);

        var totalKeywords = FirstNonEmpty(ReadString(item, "totalRowKeywords"), ReadString(item, "total_row_keywords"));

        var parserItem = new ParserProposalItemDto
        {
            Code = code,
            Delimiter = delimiter,
            HeaderMode = headerMode,
            HeaderRowIndex = headerRowIndex,
            SkipRows = skipRows,
            RemoveTotalRows = removeTotal,
            TotalRowKeywords = totalKeywords,
            Steps = []
        };

        if (item.TryGetProperty("steps", out var stepsElement) && stepsElement.ValueKind == JsonValueKind.Array)
        {
            var stepIdx = 1;
            foreach (var stepItem in stepsElement.EnumerateArray())
            {
                var kind = ReadString(stepItem, "kind");
                var source = ReadString(stepItem, "source");
                var label = ReadString(stepItem, "label");
                var id = ReadString(stepItem, "id");
                if (string.IsNullOrEmpty(id)) id = $"step-{stepIdx++}";
                if (string.IsNullOrEmpty(label)) label = $"{kind} on {source}";

                var stepParams = new Dictionary<string, string>();
                if (stepItem.TryGetProperty("params", out var paramsElement) && paramsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in paramsElement.EnumerateObject())
                    {
                        stepParams[prop.Name] = prop.Value.ToString();
                    }
                }

                if (!string.IsNullOrEmpty(kind) && !string.IsNullOrEmpty(source))
                {
                    parserItem.Steps.Add(new ParserStepProposalDto
                    {
                        Id = id,
                        Kind = kind,
                        Enabled = true,
                        Label = label,
                        Source = source,
                        Params = stepParams
                    });
                }
            }
        }

        return parserItem;
    }

    private static AiFallbackDecisionDto ParseFallbackDecisionJson(JsonElement root)
    {
        var canRecover = false;
        if (root.TryGetProperty("canRecover", out var cr)) canRecover = cr.ValueKind == JsonValueKind.True;
        else if (root.TryGetProperty("can_recover", out var cr2)) canRecover = cr2.ValueKind == JsonValueKind.True;

        var decision = new AiFallbackDecisionDto
        {
            CanRecover = canRecover,
            Diagnosis = ReadString(root, "diagnosis"),
            ActionTaken = FirstNonEmpty(ReadString(root, "actionTaken"), ReadString(root, "action_taken")),
            UserMessage = FirstNonEmpty(ReadString(root, "userMessage"), ReadString(root, "user_message")),
            FilesToSkip = ReadStrings(root, "filesToSkip").Concat(ReadStrings(root, "files_to_skip")).Distinct().ToList(),
            AdjustedParsers = []
        };

        var parsersElement = root.TryGetProperty("adjustedParsers", out var ap) ? ap
            : (root.TryGetProperty("adjusted_parsers", out var ap2) ? ap2 : default);

        if (parsersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in parsersElement.EnumerateArray())
            {
                var parserItem = ParseParserItem(item);
                if (!string.IsNullOrEmpty(parserItem.Code))
                {
                    decision.AdjustedParsers.Add(parserItem);
                }
            }
        }

        return decision;
    }

    private static AiFallbackDecisionDto FallbackDecideAnomaly(
        AiFallbackRequestDto input,
        List<string> filesToSkip,
        List<FileAnomalyDto> nonCorruptFiles)
    {
        var adjustedParsers = new List<ParserProposalItemDto>();

        foreach (var file in nonCorruptFiles)
        {
            var lines = (file.RawSnippet ?? "")
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
            {
                if (file.FileSize == 0 || file.FileSize < 50)
                {
                    if (!filesToSkip.Contains(file.FileName))
                    {
                        filesToSkip.Add(file.FileName);
                    }
                }
                else
                {
                    adjustedParsers.Add(new ParserProposalItemDto
                    {
                        Code = file.ParserCode,
                        Delimiter = ",",
                        HeaderMode = "firstRow",
                        HeaderRowIndex = 0,
                        SkipRows = 0,
                        RemoveTotalRows = false,
                        TotalRowKeywords = "",
                        Steps = []
                    });
                }
                continue;
            }

            var line0 = lines[0];
            var delimiter = ",";
            if (line0.Count(c => c == ',') >= 2) delimiter = ",";
            else if (line0.Count(c => c == '\t') >= 2) delimiter = "\t";
            else if (line0.Count(c => c == '|') >= 2) delimiter = "|";
            else if (line0.Count(c => c == ';') >= 2) delimiter = ";";

            var tokens = line0.Split(delimiter[0]);
            var numericTokens = tokens.Count(t => System.Text.RegularExpressions.Regex.IsMatch(t.Trim(), @"^(\d+|\d+[\./\-]\d+|[A-Z0-9_]{1,4})$"));
            var isHeaderless = tokens.Length > 0 && ((double)numericTokens / tokens.Length >= 0.35);

            var steps = InferFileSteps(lines, isHeaderless ? "none" : "firstRow", 0, delimiter[0]);

            adjustedParsers.Add(new ParserProposalItemDto
            {
                Code = file.ParserCode,
                Delimiter = delimiter,
                HeaderMode = isHeaderless ? "none" : "firstRow",
                HeaderRowIndex = 0,
                SkipRows = 0,
                RemoveTotalRows = false,
                TotalRowKeywords = "",
                Steps = steps
            });
        }

        var canRecover = filesToSkip.Count > 0 || adjustedParsers.Count > 0;
        var diagnosis = filesToSkip.Count > 0 && adjustedParsers.Count > 0
            ? $"Identified {filesToSkip.Count} empty file(s) and {adjustedParsers.Count} file(s) with headerless/raw formatting."
            : (filesToSkip.Count > 0
                ? $"Identified {filesToSkip.Count} empty file(s) containing no data rows."
                : "Detected headerless data rows in uploaded file(s).");

        var userMessage = filesToSkip.Count > 0 && adjustedParsers.Count > 0
            ? $"AI Decision: Automatically bypassed empty file(s) ({string.Join(", ", filesToSkip)}) and re-configured parser to headerless mode ('none') for {string.Join(", ", adjustedParsers.Select(p => p.Code))}."
            : (filesToSkip.Count > 0
                ? $"AI Decision: Automatically bypassed empty file(s) ({string.Join(", ", filesToSkip)}) that contain no data records. Reconciliation continues with valid files."
                : "AI Decision: Configured headerMode='none' to retain all rows as transactional records without requiring a header row.");

        return new AiFallbackDecisionDto
        {
            CanRecover = canRecover,
            Diagnosis = diagnosis,
            ActionTaken = filesToSkip.Count > 0 ? "skip_empty_files" : "retry_with_none_header",
            UserMessage = userMessage,
            FilesToSkip = filesToSkip,
            AdjustedParsers = adjustedParsers
        };
    }

    private static ParsersProposalDto FallbackAnalyzeParsers(List<FileSnippetDto> files)
    {
        var result = new ParsersProposalDto();
        foreach (var file in files)
        {
            var lines = (file.RawSnippet ?? "")
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
            {
                result.Parsers.Add(new ParserProposalItemDto { Code = file.Code });
                continue;
            }

            var delimiters = new[] { ',', '|', '\t', ';' };
            var bestDelimiter = ',';
            var maxCount = 0;
            foreach (var d in delimiters)
            {
                var count = lines.Take(5).Sum(l => l.Count(c => c == d));
                if (count > maxCount)
                {
                    maxCount = count;
                    bestDelimiter = d;
                }
            }

            var headerRowIndex = 0;
            var headerMode = "none";
            for (var i = 0; i < Math.Min(lines.Count, 10); i++)
            {
                var line = lines[i];
                if (line.StartsWith("===") || line.StartsWith("---") || line.StartsWith("###") || line.StartsWith("***"))
                    continue;

                var tokens = line.Split(bestDelimiter);
                if (tokens.Length < 2) continue;

                var numericCount = tokens.Count(t => System.Text.RegularExpressions.Regex.IsMatch(t.Trim(), @"^(-?\d+(\.\d+)?|\d{8}|\d{4}[-/]\d{2}[-/]\d{2})$"));
                if (numericCount >= tokens.Length * 0.4)
                {
                    headerMode = "none";
                    headerRowIndex = 0;
                    break;
                }

                var alphaCount = tokens.Count(t => System.Text.RegularExpressions.Regex.IsMatch(t, "[A-Za-z]"));
                if (alphaCount >= tokens.Length * 0.4)
                {
                    headerRowIndex = i;
                    headerMode = i > 0 ? "rowIndex" : "firstRow";
                    break;
                }
            }

            var removeTotal = false;
            var totalKeywords = "";
            var bottomLines = lines.TakeLast(5).ToList();
            if (bottomLines.Any(l => l.Contains("TOTAL", StringComparison.OrdinalIgnoreCase) ||
                                     l.Contains("COUNT", StringComparison.OrdinalIgnoreCase) ||
                                     l.Contains("***", StringComparison.OrdinalIgnoreCase) ||
                                     l.Contains("BATCH", StringComparison.OrdinalIgnoreCase)))
            {
                removeTotal = true;
                totalKeywords = "TOTAL,COUNT,SUM,***,BATCH";
            }

            var steps = InferFileSteps(lines, headerMode, headerRowIndex, bestDelimiter);

            var proposal = new ParserProposalItemDto
            {
                Code = file.Code,
                Delimiter = bestDelimiter.ToString(),
                HeaderMode = headerMode,
                HeaderRowIndex = headerMode == "none" ? 0 : headerRowIndex,
                SkipRows = headerMode == "none" ? 0 : headerRowIndex,
                RemoveTotalRows = removeTotal,
                TotalRowKeywords = totalKeywords,
                Steps = steps
            };

            result.Parsers.Add(proposal);
        }

        return result;
    }

    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == delimiter && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        result.Add(current.ToString().Trim());
        return result;
    }

    private static List<ParserStepProposalDto> InferFileSteps(
        List<string> lines,
        string headerMode,
        int headerRowIndex,
        char delimiter)
    {
        var steps = new List<ParserStepProposalDto>();
        if (lines.Count == 0) return steps;

        var dataLines = headerMode == "none"
            ? lines.Take(12).ToList()
            : lines.Skip(headerRowIndex + 1).Take(12).ToList();

        if (dataLines.Count == 0) return steps;

        var parsedDataLines = dataLines.Select(l => SplitCsvLine(l, delimiter)).ToList();
        var maxCols = parsedDataLines.Max(parts => parts.Count);
        var headerCols = headerMode == "none"
            ? Enumerable.Range(1, maxCols).Select(i => $"column_{i}").ToList()
            : (headerRowIndex < lines.Count ? SplitCsvLine(lines[headerRowIndex], delimiter) : []);

        var stepSeq = 1;
        for (var c = 0; c < headerCols.Count; c++)
        {
            var col = headerCols[c];
            var sampleVals = parsedDataLines
                .Where(parts => parts.Count > c)
                .Select(parts => parts[c].Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            if (sampleVals.Count == 0) continue;

            // 1. Date Detection
            if (System.Text.RegularExpressions.Regex.IsMatch(col, "(?i)(date|time|dt)") ||
                (headerMode == "none" && sampleVals.All(v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{8}$"))))
            {
                var format = "%Y-%m-%d";
                if (sampleVals.All(v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{8}$")))
                {
                    var first = sampleVals[0];
                    if (int.TryParse(first[..2], out var m) && m is >= 1 and <= 12 &&
                        int.TryParse(first.Substring(2, 2), out var d) && d is >= 1 and <= 31 &&
                        int.TryParse(first.Substring(4, 4), out var y) && y is >= 2020 and <= 2035)
                    {
                        format = "%m%d%Y";
                    }
                    else if (int.TryParse(first[..4], out var y2) && y2 is >= 2020 and <= 2035)
                    {
                        format = "%Y%m%d";
                    }
                    else
                    {
                        format = "%d%m%Y";
                    }
                }
                else if (sampleVals.All(v => v.Contains('/')))
                {
                    format = "%m/%d/%Y";
                }
                else if (sampleVals.All(v => v.Contains('-') && !v.StartsWith("202")))
                {
                    format = "%m-%d-%Y";
                }

                if (format != "%Y-%m-%d")
                {
                    steps.Add(new ParserStepProposalDto
                    {
                        Id = $"step-{stepSeq++}",
                        Kind = "parseDate",
                        Enabled = true,
                        Label = $"Parse {col}",
                        Source = col,
                        Params = new Dictionary<string, string> { ["format"] = format }
                    });
                }
            }
            // 2. Card Number Detection
            else if ((System.Text.RegularExpressions.Regex.IsMatch(col, "(?i)(card|pan)") &&
                      !System.Text.RegularExpressions.Regex.IsMatch(col, "(?i)(brand|type|scheme|name|holder)")) ||
                     (headerMode == "none" && sampleVals.All(v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^0*\d{15,19}$") && !v.Contains('.'))))
            {
                steps.Add(new ParserStepProposalDto
                {
                    Id = $"step-{stepSeq++}",
                    Kind = "maskCard",
                    Enabled = true,
                    Label = $"Mask {col}",
                    Source = col,
                    Params = new Dictionary<string, string> { ["keepStart"] = "6", ["keepEnd"] = "4", ["maskCharacter"] = "*" }
                });
            }
            // 3. Amount / Centavos Detection
            else if (System.Text.RegularExpressions.Regex.IsMatch(col, "(?i)(amt|amount|gross|fee|price|net|mdr)") ||
                     (headerMode == "none" && (c == 5 || c == 6 || c == 14) && sampleVals.All(v => System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d+$") && v.Length >= 4)))
            {
                if (sampleVals.All(v => !v.Contains('.') && System.Text.RegularExpressions.Regex.IsMatch(v, @"^00\d{4,}$")))
                {
                    steps.Add(new ParserStepProposalDto
                    {
                        Id = $"step-{stepSeq++}",
                        Kind = "scaleCents",
                        Enabled = true,
                        Label = $"Scale {col} from cents",
                        Source = col,
                        Params = new Dictionary<string, string> { ["scaleFactor"] = "0.01" }
                    });
                }
                else if (sampleVals.Any(v => v.Contains(',') || v.Contains('$') || v.Contains("PHP", StringComparison.OrdinalIgnoreCase)))
                {
                    steps.Add(new ParserStepProposalDto
                    {
                        Id = $"step-{stepSeq++}",
                        Kind = "replace",
                        Enabled = true,
                        Label = $"Remove currency symbols and commas from {col}",
                        Source = col,
                        Params = new Dictionary<string, string> { ["pattern"] = "[,\\$]", ["replacement"] = "" }
                    });
                }
            }
        }

        return steps;
    }
}
