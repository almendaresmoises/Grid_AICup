using System.Collections.Generic;
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
