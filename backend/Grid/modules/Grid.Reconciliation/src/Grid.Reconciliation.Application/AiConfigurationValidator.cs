namespace Grid.Reconciliation;

/// <summary>Uses the actual canonical rows, never browser-invented samples.</summary>
public static class AiConfigurationValidator
{
    public static ConfigurationValidationDto Assess(ProfileDefinition profile,
        Dictionary<string, List<Dictionary<string, string>>> rows, bool preview = true)
    {
        var assessment = new ConfigurationValidationDto();
        try { ReconciliationEngine.ValidateProfile(profile, rows); }
        catch (ReconciliationException ex) { assessment.Errors.Add(ex.Message); return assessment; }

        foreach (var (code, records) in rows)
        {
            foreach (var name in records[0].Keys)
                assessment.Columns.Add(new ConfigurationColumnDto
                {
                    Dataset = code, Column = name, Total = records.Count,
                    Missing = records.Count(row => !row.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value)),
                    Distinct = records.Select(row => row.GetValueOrDefault(name, "").Trim()).Where(value => value.Length > 0).Distinct().Count(),
                });
        }
        // Business scope cannot disappear in a later pass. A missing scope is
        // data quality, never a reason to silently match across companies/stores.
        var scopes = new[] { "company_code", "branch_code" };
        var full = profile.Rules.FirstOrDefault(rule => rule.Type == "exact");
        if (full == null || profile.Rules[0].Type != "exact")
            assessment.Errors.Add("Start automatic configuration with an exact matching pass.");
        foreach (var rule in profile.Rules)
        {
            if (rule.Type == "partial")
            {
                if (rule.Keys.Any(key => (key.Type is "amount" or "date") && (key.MatchMode is "fuzzy" or "partial") && key.Tolerance == null))
                    assessment.Errors.Add($"{rule.Id} must declare an absolute tolerance for fuzzy amounts and dates.");
                if (rule.Keys.Count(key => (key.MatchMode is null or "exact") && key.ExpectedMismatch != true) < 2)
                    assessment.Errors.Add($"{rule.Id} needs at least two exact anchors.");
                if (rule.Keys.Any(key => key.ExpectedMismatch == true || key.MatchMode == "divergent"))
                    assessment.Errors.Add($"{rule.Id} allows unbounded divergence. Use an explicit comparison and tolerance for automatic configuration.");
                if (full != null)
                    foreach (var required in full.Keys)
                        if (!rule.Keys.Any(key => key.Type == required.Type && required.Fields.All(field => key.Fields.GetValueOrDefault(field.Key) == field.Value)))
                            assessment.Errors.Add($"{rule.Id} drops a full-match key. Declare how each full-match key is compared.");
            }
            foreach (var code in rule.Datasets)
            {
                foreach (var scope in scopes.Where(scope => rows[code][0].ContainsKey(scope)))
                {
                    if (!rule.Keys.Any(key => key.Fields.GetValueOrDefault(code) == scope &&
                        (key.MatchMode is null or "exact") && key.ExpectedMismatch != true))
                        assessment.Errors.Add($"{rule.Id} must preserve exact {code}.{scope} scope.");
                }
            }
            foreach (var key in rule.Keys)
            foreach (var (code, column) in key.Fields)
            {
                var evidence = assessment.Columns.Single(c => c.Dataset == code && c.Column == column);
                var invalid = rows[code].Count(row => !string.IsNullOrWhiteSpace(row.GetValueOrDefault(column)) &&
                    !ReconciliationEngine.IsValidKeyValue(row.GetValueOrDefault(column), key, code));
                if (invalid > 0)
                    assessment.Errors.Add($"{rule.Id}: {code}.{column} contains {invalid} values incompatible with key type {key.Type}.");
                if (evidence.Missing == evidence.Total)
                    assessment.Errors.Add($"{rule.Id}: {code}.{column} is empty in every parsed record.");
                else if (evidence.Missing > 0)
                    assessment.Warnings.Add($"{code}.{column}: {evidence.Missing} of {evidence.Total} values are missing; these records cannot fully match.");
            }
        }
        assessment.Errors = assessment.Errors.Distinct().ToList();
        assessment.Warnings = assessment.Warnings.Distinct().ToList();
        if (assessment.Errors.Count > 0) return assessment;
        if (!preview) { assessment.Passed = true; return assessment; }
        try
        {
            var result = ReconciliationEngine.Reconcile(rows, profile);
            assessment.TotalRecords = result.Statistics.TotalRecords;
            assessment.PartialMatchRecords = result.MatchedGroups.Where(g => g.Evidence.Comparison == "partial_match").Sum(g => g.Records.Sum(r => r.Value.Count));
            assessment.FullMatchRecords = result.Statistics.MatchedRecords - assessment.PartialMatchRecords;
            assessment.ExceptionRecords = result.Statistics.UnresolvedRecords;
            assessment.Passes = profile.Rules.Select(rule => new ConfigurationPassDto
            {
                Rule = rule.Id,
                Groups = result.MatchedGroups.Count(group => group.Rule == rule.Id),
                Records = result.MatchedGroups.Where(group => group.Rule == rule.Id).Sum(group => group.Records.Sum(pair => pair.Value.Count)),
            }).ToList();
            foreach (var pass in assessment.Passes.Where(pass => pass.Groups == 0))
                assessment.Warnings.Add($"{pass.Rule} contributes no matches on the uploaded files.");
            if (result.Statistics.AmbiguousRecords > 0)
                assessment.Warnings.Add($"{result.Statistics.AmbiguousRecords} records have ambiguous candidates and remain exceptions.");
            assessment.Passed = true;
        }
        catch (ReconciliationException ex) { assessment.Errors.Add(ex.Message); }
        return assessment;
    }
}
