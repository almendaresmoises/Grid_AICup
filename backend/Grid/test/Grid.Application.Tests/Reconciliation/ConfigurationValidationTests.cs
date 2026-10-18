using System.Collections.Generic;
using Grid.Reconciliation;
using Xunit;

namespace Grid;

public class ConfigurationValidationTests
{
    private static Dictionary<string, List<Dictionary<string, string>>> Rows(string company = "101") => new()
    {
        ["POS"] = [new() { ["id"] = "001", ["amount"] = "100", ["company_code"] = company }],
        ["BANK"] = [new() { ["id"] = "001", ["amount"] = "100", ["company_code"] = company }],
    };

    private static ProfileDefinition Profile() => ProfileJson.Parse("""
        {"name":"Validated","datasets":["POS","BANK"],"rules":[
          {"id":"FULL","type":"exact","datasets":["POS","BANK"],"keys":[
            {"type":"text","fields":{"POS":"id","BANK":"id"}},
            {"type":"amount","fields":{"POS":"amount","BANK":"amount"}},
            {"type":"text","fields":{"POS":"company_code","BANK":"company_code"}}]}]}
        """);

    [Fact]
    public void Empty_required_matching_column_is_rejected_even_when_schema_exists()
    {
        var report = AiConfigurationValidator.Assess(Profile(), Rows(""));
        Assert.False(report.Passed);
        Assert.Contains(report.Errors, error => error.Contains("empty in every parsed record"));
    }

    [Fact]
    public void Scope_cannot_disappear_from_later_pass()
    {
        var profile = Profile();
        var partial = Profile().Rules[0];
        partial.Id = "RELAXED"; partial.Type = "partial";
        partial.Keys.RemoveAt(2);
        partial.Keys[0].MatchMode = "fuzzy";
        profile.Rules.Add(partial);
        var report = AiConfigurationValidator.Assess(profile, Rows());
        Assert.False(report.Passed);
        Assert.Contains(report.Errors, error => error.Contains("must preserve exact"));
        Assert.Contains(report.Errors, error => error.Contains("drops a full-match key"));
    }

    [Fact]
    public void Preview_accounts_for_all_combined_records_and_reports_pass_contribution()
    {
        var rows = Rows();
        rows["POS"].Add(new() { ["id"] = "002", ["amount"] = "200", ["company_code"] = "101" });
        var report = AiConfigurationValidator.Assess(Profile(), rows);
        Assert.True(report.Passed);
        Assert.Equal(3, report.TotalRecords);
        Assert.Equal(2, report.FullMatchRecords);
        Assert.Equal(1, report.ExceptionRecords);
        Assert.Equal(2, report.Passes[0].Records);
    }
}
