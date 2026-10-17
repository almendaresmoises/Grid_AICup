namespace Grid.Reconciliation;

/// <summary>
/// Implements true Damerau-Levenshtein distance and similarity scoring.
/// Supports insertions, deletions, substitutions, and adjacent transpositions.
/// </summary>
public static class DamerauLevenshtein
{
    public static int Distance(string? s, string? t)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.IsNullOrEmpty(t) ? 0 : t.Length;
        }

        if (string.IsNullOrEmpty(t))
        {
            return s.Length;
        }

        var boundsS = s.Length;
        var boundsT = t.Length;

        var d = new int[boundsS + 2, boundsT + 2];
        var maxDist = boundsS + boundsT;
        d[0, 0] = maxDist;

        for (var i = 0; i <= boundsS; i++)
        {
            d[i + 1, 0] = maxDist;
            d[i + 1, 1] = i;
        }

        for (var j = 0; j <= boundsT; j++)
        {
            d[0, j + 1] = maxDist;
            d[1, j + 1] = j;
        }

        var lastRow = new Dictionary<char, int>();

        for (var i = 1; i <= boundsS; i++)
        {
            var db = 0;
            for (var j = 1; j <= boundsT; j++)
            {
                var i1 = lastRow.TryGetValue(t[j - 1], out var val) ? val : 0;
                var j1 = db;
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                if (cost == 0)
                {
                    db = j;
                }

                d[i + 1, j + 1] = Math.Min(
                    d[i, j] + cost, // substitution
                    Math.Min(
                        d[i + 1, j] + 1, // insertion
                        Math.Min(
                            d[i, j + 1] + 1, // deletion
                            d[i1, j1] + (i - i1 - 1) + 1 + (j - j1 - 1) // transposition
                        )
                    )
                );
            }

            lastRow[s[i - 1]] = i;
        }

        return d[boundsS + 1, boundsT + 1];
    }

    public static decimal Similarity(string? s, string? t)
    {
        if (s == null && t == null)
        {
            return 1.0m;
        }

        if (s == null || t == null)
        {
            return 0.0m;
        }

        var trimmedS = s.Trim();
        var trimmedT = t.Trim();
        if (trimmedS.Length == 0 && trimmedT.Length == 0)
        {
            return 1.0m;
        }

        if (trimmedS.Length == 0 || trimmedT.Length == 0)
        {
            return 0.0m;
        }

        if (string.Equals(trimmedS, trimmedT, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0m;
        }

        var maxLen = Math.Max(trimmedS.Length, trimmedT.Length);
        var dist = Distance(trimmedS.ToUpperInvariant(), trimmedT.ToUpperInvariant());
        var sim = 1.0m - ((decimal)dist / maxLen);
        return sim < 0m ? 0m : Math.Round(sim, 4);
    }
}
