using System.Collections.Generic;
using System.Linq;
using Grid.Reconciliation;
using Xunit;

namespace Grid.Reconciliation;

public class ReconciliationEngineTests
{
    private const string PosBatch1 = """
        sale_ref,gross_amount,sale_date
        G1001,120.00,2026-10-14
        G1002,250.00,2026-10-14
        G1003,89.50,2026-10-14
        """;

    private const string PaymentBatch1 = """
        processor_ref,charge_amount,processed_date
        G1001,120,2026-10-14
        G1002,250.00,2026-10-14
        G1003,89.50,2026-10-14
        """;

    private const string BankBatch1 = """
        bank_reference,deposit_amount,deposit_date
        CARD/G1001,120.00,2026-10-14
        CARD/G1002,250,2026-10-14
        CARD/G9999,44.00,2026-10-14
        """;

    private const string PosBatch2 = """
        sale_ref,gross_amount,sale_date
        G1004,150,2026-10-15
        G1005,350,2026-10-15
        G1006,80,2026-10-15
        """;

    private const string PaymentBatch2 = """
        processor_ref,charge_amount,processed_date
        G1004,150,2026-10-15
        G1005,350,2026-10-15
        G1006,80,2026-10-15
        """;

    private const string BankBatch2 = """
        bank_reference,deposit_amount,deposit_date
        BANK/G1004,150,2026-10-15
        BANK/G1005,350,2026-10-15
        """;

    private const string DemoProfile = """
        {
          "name": "Three-way POS / processor / bank",
          "version": 1,
          "datasets": ["POS", "PAYMENT", "BANK"],
          "rules": [
            {
              "id": "3WAY_REFERENCE_AMOUNT",
              "type": "exact",
              "datasets": ["POS", "PAYMENT", "BANK"],
              "keys": [
                {
                  "type": "text",
                  "fields": {"POS": "sale_ref", "PAYMENT": "processor_ref", "BANK": "bank_reference"},
                  "strip_prefixes": {"BANK": ["CARD/"]}
                },
                {
                  "type": "amount",
                  "fields": {"POS": "gross_amount", "PAYMENT": "charge_amount", "BANK": "deposit_amount"}
                },
                {
                  "type": "date",
                  "fields": {"POS": "sale_date", "PAYMENT": "processed_date", "BANK": "deposit_date"}
                }
              ]
            }
          ]
        }
        """;

    private const string AggregationProfile = """
        {
          "name": "POS batches vs bank aggregate deposits",
          "version": 1,
          "datasets": ["POS", "BANK"],
          "rules": [
            {
              "id": "BATCH_AGGREGATE",
              "type": "aggregate",
              "datasets": ["POS", "BANK"],
              "target": "BANK",
              "source": "POS",
              "target_amount": "deposit_amount",
              "source_amount": "gross_amount",
              "tolerance": "0.00",
              "keys": [{"type": "text", "fields": {"POS": "settlement_batch", "BANK": "bank_batch"}}]
            }
          ]
        }
        """;

    private const string PosAggregate = """
        receipt_id,settlement_batch,gross_amount
        P001,B-01,40.00
        P002,B-01,60.00
        P003,B-02,85.00
        P004,B-02,15.00
        P005,B-03,12.00
        """;

    private const string BankAggregate = """
        bank_batch,deposit_amount
        B-01,100.00
        B-02,100.00
        B-03,11.50
        """;

    [Fact]
    public void Initial_batch_matches_two_groups()
    {
        var result = ReconciliationEngine.Reconcile(Batch(1), ProfileJson.Parse(DemoProfile));

        Assert.Equal(9, result.Statistics.TotalRecords);
        Assert.Equal(6, result.Statistics.MatchedRecords);
        Assert.Equal(3, result.Statistics.UnresolvedRecords);
        Assert.Equal(0, result.Statistics.AmbiguousRecords);
        Assert.Equal(2, result.Statistics.MatchedGroups);
    }

    [Fact]
    public void Next_batch_needs_a_prefix_and_preserves_the_original()
    {
        var data = Batch(2);
        var oldProfile = ProfileJson.Parse(DemoProfile);
        var old = ReconciliationEngine.Reconcile(data, oldProfile);
        Assert.Equal(0, old.Statistics.MatchedRecords);

        var changed = ProfileJson.Parse(DemoProfile);
        changed.Rules[0].Keys[0].StripPrefixes!["BANK"].Add("BANK/");
        var revised = ReconciliationEngine.Reconcile(data, changed);
        Assert.Equal(6, revised.Statistics.MatchedRecords);
        Assert.Equal(2, revised.Statistics.UnresolvedRecords);
        Assert.Equal(6, ReconciliationEngine.Reconcile(Batch(1), changed).Statistics.MatchedRecords);

        var baseline = ReconciliationEngine.Reconcile(Batch(1), oldProfile);
        Assert.True(ReconciliationEngine.PreservesMatches(baseline, ReconciliationEngine.Reconcile(Batch(1), changed)));
    }

    [Fact]
    public void Duplicate_signature_is_not_matched()
    {
        var data = Batch(1);
        data["POS"].Add(new Dictionary<string, string>(data["POS"][0]));
        var result = ReconciliationEngine.Reconcile(data, ProfileJson.Parse(DemoProfile));

        Assert.True(result.Statistics.AmbiguousRecords >= 3);
        Assert.Equal(3, result.Statistics.MatchedRecords);
    }

    [Fact]
    public void Unknown_field_is_rejected()
    {
        var profile = ProfileJson.Parse(DemoProfile);
        profile.Rules[0].Keys[0].Fields["BANK"] = "imaginary";

        var error = Assert.Throws<ReconciliationException>(() => ReconciliationEngine.Reconcile(Batch(1), profile));
        Assert.Contains("Unknown column", error.Message);
    }

    [Fact]
    public void Empty_keys_do_not_match()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["A"] = [new() { ["ref"] = "", ["amount"] = "5" }],
            ["B"] = [new() { ["ref"] = "", ["amount"] = "5" }],
        };
        var profile = ProfileJson.Parse("""
            {"datasets":["A","B"],"rules":[{"id":"K","type":"exact","datasets":["A","B"],"keys":[{"type":"text","fields":{"A":"ref","B":"ref"}}]}]}
            """);

        Assert.Equal(0, ReconciliationEngine.Reconcile(data, profile).Statistics.MatchedRecords);
    }

    [Fact]
    public void Many_to_one_aggregation_matches_when_the_sum_agrees()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["POS"] =
            [
                new() { ["batch"] = "A", ["amt"] = "12.50" },
                new() { ["batch"] = "A", ["amt"] = "7.5" },
            ],
            ["BANK"] = [new() { ["deposit_ref"] = "A", ["settled"] = "20.00" }],
        };
        var profile = ProfileJson.Parse("""
            {"datasets":["POS","BANK"],"rules":[{"id":"AGG","type":"aggregate","datasets":["POS","BANK"],"target":"BANK","source":"POS","target_amount":"settled","source_amount":"amt","tolerance":"0","keys":[{"type":"text","fields":{"POS":"batch","BANK":"deposit_ref"}}]}]}
            """);

        Assert.Equal(3, ReconciliationEngine.Reconcile(data, profile).Statistics.MatchedRecords);
    }

    [Fact]
    public void Amount_variance_blocks_aggregation()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["POS"] =
            [
                new() { ["batch"] = "A", ["amt"] = "12.50" },
                new() { ["batch"] = "A", ["amt"] = "7.5" },
            ],
            ["BANK"] = [new() { ["deposit_ref"] = "A", ["settled"] = "21.00" }],
        };
        var profile = ProfileJson.Parse("""
            {"datasets":["POS","BANK"],"rules":[{"id":"AGG","type":"aggregate","datasets":["POS","BANK"],"target":"BANK","source":"POS","target_amount":"settled","source_amount":"amt","tolerance":"0","keys":[{"type":"text","fields":{"POS":"batch","BANK":"deposit_ref"}}]}]}
            """);

        Assert.Equal(0, ReconciliationEngine.Reconcile(data, profile).Statistics.MatchedRecords);
    }

    [Fact]
    public void Duplicate_csv_headers_are_rejected()
    {
        var error = Assert.Throws<ReconciliationException>(() => ReconciliationEngine.ReadCsv("amount,amount\n10,10\n"));
        Assert.Contains("duplicate column", error.Message);
    }

    [Fact]
    public void Example_aggregation_batch_leaves_the_short_deposit()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["POS"] = ReconciliationEngine.ReadCsv(PosAggregate),
            ["BANK"] = ReconciliationEngine.ReadCsv(BankAggregate),
        };
        var stats = ReconciliationEngine.Reconcile(data, ProfileJson.Parse(AggregationProfile)).Statistics;

        Assert.Equal(8, stats.TotalRecords);
        Assert.Equal(6, stats.MatchedRecords);
        Assert.Equal(2, stats.UnresolvedRecords);
    }

    [Fact]
    public void Partial_pass_matches_with_damerau_levenshtein_transposition()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["POS"] = [new() { ["account"] = "ACC-01", ["ref"] = "INV-90210", ["amount"] = "150.00" }],
            ["BANK"] = [new() { ["account_num"] = "ACC-01", ["tx_ref"] = "INV-92010", ["deposit"] = "150.00" }],
        };
        var profile = ProfileJson.Parse("""
            {
              "datasets": ["POS", "BANK"],
              "rules": [
                {
                  "id": "PARTIAL_FUZZY",
                  "type": "partial",
                  "datasets": ["POS", "BANK"],
                  "min_score": 0.85,
                  "keys": [
                    {"type": "text", "fields": {"POS": "account", "BANK": "account_num"}, "match_mode": "exact"},
                    {"type": "amount", "fields": {"POS": "amount", "BANK": "deposit"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "ref", "BANK": "tx_ref"}, "match_mode": "fuzzy", "algorithm": "damerau_levenshtein", "min_score": 0.85}
                  ]
                }
              ]
            }
            """);

        var result = ReconciliationEngine.Reconcile(data, profile);

        Assert.Equal(1, result.Statistics.MatchedGroups);
        Assert.Equal(2, result.Statistics.MatchedRecords);
        var match = result.MatchedGroups[0];
        Assert.Equal("partial", match.RuleType);
        Assert.True(match.Score >= 0.85m);
        Assert.Contains("Possible match", match.Remarks);
        Assert.Contains("INV-90210", match.Remarks);
        Assert.Contains("INV-92010", match.Remarks);
    }

    [Fact]
    public void Partial_pass_strict_min_score_prevents_false_positives()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["POS"] = [new() { ["account"] = "ACC-01", ["ref"] = "INV-90210", ["amount"] = "150.00" }],
            ["BANK"] = [new() { ["account_num"] = "ACC-01", ["tx_ref"] = "INV-12345", ["deposit"] = "150.00" }],
        };
        var profile = ProfileJson.Parse("""
            {
              "datasets": ["POS", "BANK"],
              "rules": [
                {
                  "id": "PARTIAL_STRICT",
                  "type": "partial",
                  "datasets": ["POS", "BANK"],
                  "min_score": 0.85,
                  "keys": [
                    {"type": "text", "fields": {"POS": "account", "BANK": "account_num"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "ref", "BANK": "tx_ref"}, "match_mode": "fuzzy", "algorithm": "damerau_levenshtein", "min_score": 0.85}
                  ]
                }
              ]
            }
            """);

        var result = ReconciliationEngine.Reconcile(data, profile);

        Assert.Equal(0, result.Statistics.MatchedGroups);
        Assert.Equal(2, result.Statistics.UnresolvedRecords);
    }

    [Fact]
    public void Partial_pass_numeric_best_practice_matches_within_tolerance()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["POS"] = [new() { ["ref"] = "REF-888", ["amount"] = "100.00" }],
            ["BANK"] = [new() { ["ref"] = "REF-888", ["amount"] = "99.95" }],
        };
        var profile = ProfileJson.Parse("""
            {
              "datasets": ["POS", "BANK"],
              "rules": [
                {
                  "id": "PARTIAL_NUMERIC",
                  "type": "partial",
                  "datasets": ["POS", "BANK"],
                  "keys": [
                    {"type": "text", "fields": {"POS": "ref", "BANK": "ref"}, "match_mode": "exact"},
                    {"type": "amount", "fields": {"POS": "amount", "BANK": "amount"}, "match_mode": "fuzzy", "algorithm": "numeric_best_practice", "tolerance": "0.10", "min_score": 0.70}
                  ]
                }
              ]
            }
            """);

        var result = ReconciliationEngine.Reconcile(data, profile);

        Assert.Equal(1, result.Statistics.MatchedGroups);
        Assert.True(result.MatchedGroups[0].Score >= 0.70m);
        Assert.Contains("amount", result.MatchedGroups[0].Remarks);
    }

    [Fact]
    public void Partial_pass_expected_mismatch_flags_recon_remarks()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["POS"] = [new() { ["order_id"] = "ORD-77", ["store"] = "STORE_EAST", ["amount"] = "50.00" }],
            ["BANK"] = [new() { ["order_id"] = "ORD-77", ["store"] = "STORE_WEST", ["amount"] = "50.00" }],
        };
        var profile = ProfileJson.Parse("""
            {
              "datasets": ["POS", "BANK"],
              "rules": [
                {
                  "id": "EXPECTED_DIVERGENCE",
                  "type": "partial",
                  "datasets": ["POS", "BANK"],
                  "keys": [
                    {"type": "text", "fields": {"POS": "order_id", "BANK": "order_id"}, "match_mode": "exact"},
                    {"type": "amount", "fields": {"POS": "amount", "BANK": "amount"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "store", "BANK": "store"}, "expected_mismatch": true}
                  ]
                }
              ]
            }
            """);

        var result = ReconciliationEngine.Reconcile(data, profile);

        Assert.Equal(1, result.Statistics.MatchedGroups);
        var match = result.MatchedGroups[0];
        Assert.Contains("Possible match but store is not matched", match.Remarks);
        Assert.Contains("STORE_EAST", match.Remarks);
        Assert.Contains("STORE_WEST", match.Remarks);
    }

    [Fact]
    public void Multi_pass_profile_runs_exact_first_then_partial()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["POS"] =
            [
                new() { ["id"] = "TX1", ["name"] = "Alice", ["amt"] = "10" },
                new() { ["id"] = "TX2", ["name"] = "Bbob", ["amt"] = "20" },
            ],
            ["BANK"] =
            [
                new() { ["id"] = "TX1", ["name"] = "Alice", ["amt"] = "10" },
                new() { ["id"] = "TX2", ["name"] = "Bob", ["amt"] = "20" },
            ],
        };
        var profile = ProfileJson.Parse("""
            {
              "datasets": ["POS", "BANK"],
              "rules": [
                {
                  "id": "PASS_1_EXACT",
                  "type": "exact",
                  "datasets": ["POS", "BANK"],
                  "keys": [
                    {"type": "text", "fields": {"POS": "id", "BANK": "id"}},
                    {"type": "text", "fields": {"POS": "name", "BANK": "name"}},
                    {"type": "amount", "fields": {"POS": "amt", "BANK": "amt"}}
                  ]
                },
                {
                  "id": "PASS_2_PARTIAL",
                  "type": "partial",
                  "datasets": ["POS", "BANK"],
                  "keys": [
                    {"type": "text", "fields": {"POS": "id", "BANK": "id"}, "match_mode": "exact"},
                    {"type": "amount", "fields": {"POS": "amt", "BANK": "amt"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "name", "BANK": "name"}, "match_mode": "fuzzy", "algorithm": "damerau_levenshtein", "min_score": 0.70}
                  ]
                }
              ]
            }
            """);

        var result = ReconciliationEngine.Reconcile(data, profile);

        Assert.Equal(2, result.Statistics.MatchedGroups);
        Assert.Equal(4, result.Statistics.MatchedRecords);
        Assert.Equal(0, result.Statistics.UnresolvedRecords);
        Assert.Equal("PASS_1_EXACT", result.MatchedGroups[0].Rule);
        Assert.Equal("PASS_2_PARTIAL", result.MatchedGroups[1].Rule);
        Assert.Contains("Possible match", result.MatchedGroups[1].Remarks);
    }

    [Fact]
    public void Six_pass_waterfall_cascade_reconciles_combinations_of_misses()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["POS"] =
            [
                new() { ["order_id"] = "ORD-01", ["gross_amount"] = "100.00", ["sale_date"] = "2026-10-14", ["card_last_4"] = "1234", ["terminal_id"] = "T1" },
                new() { ["order_id"] = "ORD-02", ["gross_amount"] = "200.00", ["sale_date"] = "2026-10-14", ["card_last_4"] = "1111", ["terminal_id"] = "T1" },
                new() { ["order_id"] = "ORD-03", ["gross_amount"] = "300.00", ["sale_date"] = "2026-10-14", ["card_last_4"] = "3333", ["terminal_id"] = "T1" },
                new() { ["order_id"] = "ORD-04", ["gross_amount"] = "400.00", ["sale_date"] = "2026-10-14", ["card_last_4"] = "4444", ["terminal_id"] = "T1" },
                new() { ["order_id"] = "ORD-05X", ["gross_amount"] = "500.00", ["sale_date"] = "2026-10-14", ["card_last_4"] = "5555", ["terminal_id"] = "T1" },
                new() { ["order_id"] = "ORD-06", ["gross_amount"] = "600.00", ["sale_date"] = "2026-10-14", ["card_last_4"] = "6666", ["terminal_id"] = "T1" },
            ],
            ["GATEWAY"] =
            [
                new() { ["ext_ref"] = "TXN-ORD-01", ["charge_amount"] = "100.00", ["settlement_date"] = "2026-10-14", ["card_pan_last4"] = "1234", ["pos_terminal"] = "T1" },
                new() { ["ext_ref"] = "TXN-ORD-02", ["charge_amount"] = "200.00", ["settlement_date"] = "2026-10-14", ["card_pan_last4"] = "2222", ["pos_terminal"] = "T1" },
                new() { ["ext_ref"] = "TXN-ORD-03", ["charge_amount"] = "300.00", ["settlement_date"] = "2026-10-15", ["card_pan_last4"] = "3333", ["pos_terminal"] = "T1" },
                new() { ["ext_ref"] = "TXN-ORD-04", ["charge_amount"] = "399.50", ["settlement_date"] = "2026-10-14", ["card_pan_last4"] = "4444", ["pos_terminal"] = "T1" },
                new() { ["ext_ref"] = "TXN-ORD-05", ["charge_amount"] = "500.00", ["settlement_date"] = "2026-10-14", ["card_pan_last4"] = "5555", ["pos_terminal"] = "T1" },
                new() { ["ext_ref"] = "TXN-ORD-06", ["charge_amount"] = "600.00", ["settlement_date"] = "2026-10-15", ["card_pan_last4"] = "7777", ["pos_terminal"] = "T1" },
            ]
        };

        var profile = ProfileJson.Parse("""
            {
              "name": "Cascade Reconciliation Test",
              "datasets": ["POS", "GATEWAY"],
              "rules": [
                {
                  "id": "EXACT_01_FULL",
                  "type": "exact",
                  "datasets": ["POS", "GATEWAY"],
                  "keys": [
                    {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-"]}},
                    {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}},
                    {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}},
                    {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}},
                    {"type": "text", "fields": {"POS": "terminal_id", "GATEWAY": "pos_terminal"}}
                  ]
                },
                {
                  "id": "PARTIAL_02_CARD_DIVERGENT",
                  "type": "partial",
                  "datasets": ["POS", "GATEWAY"],
                  "min_score": 0.85,
                  "remarks": "Possible match on ref, amount & date; card/account divergent",
                  "keys": [
                    {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-"]}, "match_mode": "exact"},
                    {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "exact"},
                    {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "terminal_id", "GATEWAY": "pos_terminal"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "divergent", "min_score": 0.85}
                  ]
                },
                {
                  "id": "PARTIAL_03_DATE_LAG",
                  "type": "partial",
                  "datasets": ["POS", "GATEWAY"],
                  "min_score": 0.80,
                  "remarks": "Possible match with 1-3 day date settlement difference",
                  "keys": [
                    {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-"]}, "match_mode": "exact"},
                    {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "exact"},
                    {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "fuzzy", "algorithm": "date_proximity", "min_score": 0.80}
                  ]
                },
                {
                  "id": "PARTIAL_04_AMOUNT_VARIANCE",
                  "type": "partial",
                  "datasets": ["POS", "GATEWAY"],
                  "min_score": 0.70,
                  "remarks": "Possible match on ref & date; amount variance within tolerance",
                  "keys": [
                    {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-"]}, "match_mode": "exact"},
                    {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "exact"},
                    {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "fuzzy", "algorithm": "numeric_best_practice", "tolerance": "1.00", "min_score": 0.70}
                  ]
                },
                {
                  "id": "PARTIAL_05_REF_FUZZY",
                  "type": "partial",
                  "datasets": ["POS", "GATEWAY"],
                  "min_score": 0.80,
                  "remarks": "Possible match on amount, date & card; reference has minor typo",
                  "keys": [
                    {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "exact"},
                    {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-"]}, "match_mode": "fuzzy", "algorithm": "damerau_levenshtein", "min_score": 0.80}
                  ]
                },
                {
                  "id": "PARTIAL_06_COMBINED_VARIANCE",
                  "type": "partial",
                  "datasets": ["POS", "GATEWAY"],
                  "min_score": 0.80,
                  "remarks": "Possible match on ref & amount; card divergent and date settlement difference",
                  "keys": [
                    {"type": "text", "fields": {"POS": "order_id", "GATEWAY": "ext_ref"}, "strip_prefixes": {"GATEWAY": ["TXN-"]}, "match_mode": "exact"},
                    {"type": "amount", "fields": {"POS": "gross_amount", "GATEWAY": "charge_amount"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"POS": "card_last_4", "GATEWAY": "card_pan_last4"}, "match_mode": "divergent", "min_score": 0.85},
                    {"type": "date", "fields": {"POS": "sale_date", "GATEWAY": "settlement_date"}, "match_mode": "fuzzy", "algorithm": "date_proximity", "min_score": 0.80}
                  ]
                }
              ]
            }
            """);

        ReconciliationEngine.ValidateProfile(profile, data);
        var result = ReconciliationEngine.Reconcile(data, profile);

        Assert.Equal(6, result.Statistics.MatchedGroups);
        Assert.Equal(12, result.Statistics.MatchedRecords);
        Assert.Equal(0, result.Statistics.UnresolvedRecords);

        Assert.Equal("EXACT_01_FULL", result.MatchedGroups[0].Rule);
        Assert.Equal("PARTIAL_02_CARD_DIVERGENT", result.MatchedGroups[1].Rule);
        Assert.Equal("PARTIAL_03_DATE_LAG", result.MatchedGroups[2].Rule);
        Assert.Equal("PARTIAL_04_AMOUNT_VARIANCE", result.MatchedGroups[3].Rule);
        Assert.Equal("PARTIAL_05_REF_FUZZY", result.MatchedGroups[4].Rule);
        Assert.Equal("PARTIAL_06_COMBINED_VARIANCE", result.MatchedGroups[5].Rule);

        Assert.Contains("Possible match but card_last_4 is not matched", result.MatchedGroups[1].Remarks);
        Assert.Contains("sale_date similarity is 90%", result.MatchedGroups[2].Remarks);
        Assert.Contains("gross_amount similarity is", result.MatchedGroups[3].Remarks);
        Assert.Contains("order_id similarity is", result.MatchedGroups[4].Remarks);
        Assert.Contains("Possible match but card_last_4 is not matched", result.MatchedGroups[5].Remarks);
        Assert.Contains("sale_date similarity is 90%", result.MatchedGroups[5].Remarks);
    }

    [Fact]
    public void Reconcile_AuthCodeOcrTypo_MatchesWithMinScore80()
    {
        var data = new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["PF"] =
            [
                new()
                {
                    ["app_code"] = "03655I",
                    ["gross_amt"] = "869.00",
                    ["trx_date"] = "2026-08-10",
                    ["card_number"] = "4147340000009141",
                    ["branch_code"] = "0006",
                }
            ],
            ["TTL"] =
            [
                new()
                {
                    ["reference_code"] = "036551",
                    ["gross_amt"] = "869.00",
                    ["trx_date"] = "2026-08-10",
                    ["card_number"] = "4147340000009141",
                    ["branch_code"] = "0006",
                }
            ],
        };

        var profile = ProfileJson.Parse("""
            {
              "name": "Auth code typo test",
              "version": 1,
              "datasets": ["PF", "TTL"],
              "rules": [
                {
                  "id": "PARTIAL_REF_FUZZY",
                  "type": "partial",
                  "datasets": ["PF", "TTL"],
                  "min_score": 0.80,
                  "remarks": "Possible match on card, amount, date & branch; auth code typo",
                  "keys": [
                    {"type": "amount", "fields": {"PF": "gross_amt", "TTL": "gross_amt"}, "match_mode": "exact"},
                    {"type": "date", "fields": {"PF": "trx_date", "TTL": "trx_date"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"PF": "card_number", "TTL": "card_number"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"PF": "branch_code", "TTL": "branch_code"}, "match_mode": "exact"},
                    {"type": "text", "fields": {"PF": "app_code", "TTL": "reference_code"}, "match_mode": "fuzzy", "algorithm": "damerau_levenshtein", "min_score": 0.80}
                  ]
                }
              ]
            }
            """);

        ReconciliationEngine.ValidateProfile(profile, data);
        var result = ReconciliationEngine.Reconcile(data, profile);

        Assert.Equal(1, result.Statistics.MatchedGroups);
        Assert.Equal(2, result.Statistics.MatchedRecords);
        Assert.Equal(0, result.Statistics.UnresolvedRecords);
        Assert.Equal(0.8333m, result.MatchedGroups[0].Score);
        Assert.Contains("app_code similarity is 83.3%", result.MatchedGroups[0].Remarks);
    }

    [Fact]
    public void Reconcile_EnhancedProfile_PreservesBaselineMatches()
    {
        var baselinePath = @"C:\Users\maa\.gemini\antigravity-cli\brain\44639d76-d138-40e9-9a46-35cdd56b2a95\scratch\";
        if (!System.IO.File.Exists(baselinePath + "baseline_datasets.json")) return;

        var dataJson = System.IO.File.ReadAllText(baselinePath + "baseline_datasets.json");
        var baseProfJson = System.IO.File.ReadAllText(baselinePath + "baseline_profile.json");
        var enhProfJson = System.IO.File.ReadAllText(baselinePath + "enhanced_profile.json");

        var rows = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(dataJson)!;
        var original = ProfileJson.Parse(baseProfJson);
        var proposed = ProfileJson.Parse(enhProfJson);

        var prev = ReconciliationEngine.Reconcile(rows, original);
        var next = ReconciliationEngine.Reconcile(rows, proposed);

        var preserves = ReconciliationEngine.PreservesMatches(prev, next);
        Assert.True(preserves, "Regression check failed: previously matched groups changed!");
        Assert.Equal(90, next.Statistics.UnresolvedRecords);
        Assert.Equal(6112, next.Statistics.MatchedRecords);

        var match29 = next.MatchedGroups.FirstOrDefault(g => g.Records.TryGetValue("PF_2", out var ids) && ids.Contains(29));
        Assert.NotNull(match29);
        Assert.Equal("PARTIAL_02_REF_FUZZY", match29.Rule);
        Assert.Contains(83, match29.Records["TTL_2"]);
        Assert.Equal(0.8333m, match29.Score);

        var resultJson = System.Text.Json.JsonSerializer.Serialize(next, ProfileJson.Storage);
        System.IO.File.WriteAllText(baselinePath + "csharp_run_result.json", resultJson);
    }

    private static Dictionary<string, List<Dictionary<string, string>>> Batch(int number)
    {
        if (number == 1)
        {
            return new Dictionary<string, List<Dictionary<string, string>>>
            {
                ["POS"] = ReconciliationEngine.ReadCsv(PosBatch1),
                ["PAYMENT"] = ReconciliationEngine.ReadCsv(PaymentBatch1),
                ["BANK"] = ReconciliationEngine.ReadCsv(BankBatch1),
            };
        }

        return new Dictionary<string, List<Dictionary<string, string>>>
        {
            ["POS"] = ReconciliationEngine.ReadCsv(PosBatch2),
            ["PAYMENT"] = ReconciliationEngine.ReadCsv(PaymentBatch2),
            ["BANK"] = ReconciliationEngine.ReadCsv(BankBatch2),
        };
    }
}
