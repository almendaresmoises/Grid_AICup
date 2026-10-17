namespace Grid.Reconciliation;

/// <summary>
/// Best practice numeric similarity scoring for financial and transaction reconciliation.
/// </summary>
public static class NumericMatching
{
    public static decimal Similarity(decimal val1, decimal val2, decimal? tolerance = null)
    {
        if (val1 == val2)
        {
            return 1.0m;
        }

        var diff = Math.Abs(val1 - val2);

        if (tolerance.HasValue && tolerance.Value > 0)
        {
            if (diff <= tolerance.Value)
            {
                return Math.Round(1.0m - (diff / (tolerance.Value * 2m)), 4);
            }
        }

        var maxVal = Math.Max(Math.Abs(val1), Math.Abs(val2));
        if (maxVal == 0m)
        {
            return 1.0m;
        }

        var sim = 1.0m - (diff / maxVal);
        return sim < 0m ? 0m : Math.Round(sim, 4);
    }
}
