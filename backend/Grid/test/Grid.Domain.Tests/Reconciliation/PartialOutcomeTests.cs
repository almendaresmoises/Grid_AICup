using System.Collections.Generic;
using Xunit;

namespace Grid.Reconciliation;

public class PartialOutcomeTests
{
    private static ProfileDefinition Profile(string type = "date", string field = "date", string tolerance = "2") => ProfileJson.Parse($$"""
        {"name":"Partial evidence","datasets":["A","B"],"rules":[
          {"id":"COMPARE","type":"partial","datasets":["A","B"],"min_score":0.8,"keys":[
            {"type":"text","fields":{"A":"id","B":"id"},"match_mode":"exact"},
            {"type":"{{type}}","fields":{"A":"{{field}}","B":"{{field}}"},"match_mode":"fuzzy","tolerance":"{{tolerance}}"}]}]}
        """);

    private static Dictionary<string, List<Dictionary<string, string>>> Rows(string left, string right, string field = "date") => new()
    {
        ["A"] = [new() { ["id"] = "001", [field] = left }],
        ["B"] = [new() { ["id"] = "001", [field] = right }],
    };

    [Fact]
    public void Identical_dates_in_a_partial_rule_produce_a_full_match()
    {
        var result = ReconciliationEngine.Reconcile(Rows("2026-08-10", "2026-08-10"), Profile());
        var group = Assert.Single(result.MatchedGroups);
        Assert.Equal("exact", group.RuleType);
        Assert.Equal("equal_normalized_keys", group.Evidence.Comparison);
        Assert.Null(group.Evidence.DivergentKey);
        Assert.Equal("All configured keys match exactly", group.Remarks);
    }

    [Fact]
    public void Actual_date_difference_is_partial_and_explained()
    {
        var group = Assert.Single(ReconciliationEngine.Reconcile(Rows("2026-08-10", "2026-08-11"), Profile()).MatchedGroups);
        Assert.Equal("partial", group.RuleType);
        Assert.Equal("date", group.Evidence.DivergentKey);
        Assert.Equal(0.9m, group.Score);
    }

    [Fact]
    public void Missing_values_do_not_match_each_other()
    {
        Assert.Empty(ReconciliationEngine.Reconcile(Rows("", ""), Profile()).MatchedGroups);
    }

    [Fact]
    public void Numeric_tolerance_is_an_absolute_limit_not_a_relative_similarity_hint()
    {
        var result = ReconciliationEngine.Reconcile(Rows("5051.70", "5151.70", "amount"), Profile("amount", "amount", "1.00"));
        Assert.Empty(result.MatchedGroups);
        Assert.Equal(2, result.Unresolved.Count);
    }

    [Fact]
    public void Date_tolerance_is_an_absolute_limit()
    {
        Assert.Empty(ReconciliationEngine.Reconcile(Rows("2026-08-10", "2026-08-13"), Profile()).MatchedGroups);
    }

    [Fact]
    public void Three_way_matching_checks_every_pair_and_does_not_choose_ties()
    {
        var profile = Profile();
        profile.Datasets.Add("C");
        profile.Rules[0].Datasets.Add("C");
        profile.Rules[0].Keys.ForEach(key => key.Fields["C"] = key.Fields["A"]);
        var rows = Rows("2026-08-10", "2026-08-08");
        rows["C"] = [new() { ["id"] = "001", ["date"] = "2026-08-12" }];
        Assert.Empty(ReconciliationEngine.Reconcile(rows, profile).MatchedGroups);

        foreach (var code in profile.Datasets)
            rows[code] = [new() { ["id"] = "001", ["date"] = "2026-08-10" }, new() { ["id"] = "001", ["date"] = "2026-08-10" }];
        var ambiguous = ReconciliationEngine.Reconcile(rows, profile);
        Assert.Empty(ambiguous.MatchedGroups);
        Assert.Equal(6, ambiguous.Statistics.AmbiguousRecords);
    }

    [Fact]
    public void A_pass_that_omits_full_match_requirements_cannot_claim_full_match_or_a_complete_score()
    {
        var profile = Profile();
        var full = ProfileJson.Parse(ProfileJson.Write(profile)).Rules[0];
        full.Id = "FULL"; full.Type = "exact";
        full.Keys.Add(new() { Type = "text", Fields = new() { ["A"] = "company_code", ["B"] = "company_code" } });
        profile.Rules.Insert(0, full);
        var rows = Rows("2026-08-10", "2026-08-10");
        rows["A"][0]["company_code"] = "101";
        rows["B"][0]["company_code"] = "102";
        var group = Assert.Single(ReconciliationEngine.Reconcile(rows, profile).MatchedGroups);
        Assert.Equal("partial", group.RuleType);
        Assert.Null(group.Score);
        Assert.Contains("company_code", group.Remarks);
    }
}
