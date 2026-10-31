namespace ExpenseInsights.Core.Insights;

/// <summary>
/// A personal baseline: the median of recent months and their spread (scaled MAD).
/// </summary>
/// <param name="Median">The usual amount.</param>
/// <param name="Spread">The scaled MAD: comparable to a standard deviation, but one odd month doesn't move it.</param>
/// <param name="Months">How many months it is the median of.</param>
public sealed record Baseline(decimal Median, decimal Spread, int Months);

/// <summary>A value compared with a baseline.</summary>
/// <param name="Delta">Value − median.</param>
/// <param name="Threshold">How far from the median a value has to be to be unusual.</param>
/// <param name="IsUnusual">|Δ| ≥ threshold.</param>
/// <param name="Z">The robust z-score: |Δ| measured in spreads (threshold ÷ k), capped.</param>
public sealed record Comparison(decimal Delta, decimal Threshold, bool IsUnusual, double Z)
{
    public bool IsUp => Delta > 0;
}

/// <summary>
/// The robust statistics that decide what is unusual (plan 06, decision D26): no model, just the
/// user's own history, so every threshold is reviewable and every number traceable.
/// </summary>
public static class InsightStatistics
{
    /// <summary>Makes the MAD comparable to a standard deviation for normally distributed data.</summary>
    public const decimal MadScale = 1.4826m;

    /// <summary>How many spreads away from the median a month must be to be unusual.</summary>
    public const decimal K = 3m;

    /// <summary>A change smaller than this (in the account currency) is never news.</summary>
    public const decimal MinimumDifference = 100m;

    /// <summary>
    /// The least relative change that counts: the fallback for a near-constant series (a fixed
    /// bill, whose spread is about zero), so a 1% change in rent isn't a spike.
    /// </summary>
    public const decimal MinimumRelativeChange = 0.10m;

    public const int BaselineMonths = 6;

    public const int MinimumBaselineMonths = 3;

    /// <summary>The robust z-score is capped so one extreme value doesn't drown out the rest.</summary>
    public const double ZCap = 10;

    public static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            throw new ArgumentException("The median of nothing is undefined.", nameof(values));
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>The median absolute deviation from the median, scaled by <see cref="MadScale"/>.</summary>
    public static decimal Spread(IReadOnlyCollection<decimal> values)
    {
        var median = Median(values);
        return Median(values.Select(v => Math.Abs(v - median))) * MadScale;
    }

    /// <summary>
    /// The baseline from the months before the one being judged (oldest first): the last
    /// <see cref="BaselineMonths"/> of them, and <c>null</c> with fewer than
    /// <see cref="MinimumBaselineMonths"/>.
    /// </summary>
    public static Baseline? Baseline(IReadOnlyList<decimal> previousMonths)
    {
        if (previousMonths.Count < MinimumBaselineMonths)
        {
            return null;
        }

        var window = previousMonths.Skip(Math.Max(0, previousMonths.Count - BaselineMonths)).ToArray();
        // Money: the usual amount is shown, so it is rounded to the cent (an even count's median may not be).
        return new Baseline(Math.Round(Median(window), 2), Spread(window), window.Length);
    }

    /// <summary>
    /// Unusual when |value − median| ≥ max(k × spread, 10% of the median) and ≥ the minimum
    /// difference. Volatile series need a bigger jump; a near-constant one needs 10%.
    /// </summary>
    public static Comparison Compare(decimal value, Baseline baseline, decimal minimumDifference = MinimumDifference)
    {
        var delta = value - baseline.Median;
        var threshold = Math.Max(Math.Max(K * baseline.Spread, MinimumRelativeChange * Math.Abs(baseline.Median)), minimumDifference);
        var z = threshold == 0 ? ZCap : Math.Min((double)(Math.Abs(delta) / (threshold / K)), ZCap);
        return new Comparison(delta, threshold, Math.Abs(delta) >= threshold, z);
    }

    /// <summary>
    /// The same month in earlier years (oldest first) as a reference: last year's value with one
    /// year of history, the median with two or more. <c>null</c> without any.
    /// </summary>
    public static decimal? SeasonalReference(IReadOnlyList<decimal> sameMonthInEarlierYears) =>
        sameMonthInEarlierYears.Count switch
        {
            0 => null,
            1 => sameMonthInEarlierYears[0],
            _ => Median(sameMonthInEarlierYears),
        };

    /// <summary>
    /// Compares with the baseline and, when there is one, with the seasonal reference too (using the
    /// baseline's spread): unusual only if unusual against both, in the same direction, so every
    /// December isn't news.
    /// </summary>
    public static Comparison CompareWithSeason(decimal value, Baseline baseline, decimal? seasonalReference, decimal minimumDifference = MinimumDifference)
    {
        var recent = Compare(value, baseline, minimumDifference);
        if (seasonalReference is not { } reference || !recent.IsUnusual)
        {
            return recent;
        }

        var seasonal = Compare(value, baseline with { Median = reference }, minimumDifference);
        var both = seasonal.IsUnusual && seasonal.IsUp == recent.IsUp;
        return recent with { IsUnusual = both, Z = Math.Min(recent.Z, seasonal.Z) };
    }

    /// <summary>The p-th percentile (0–1) by linear interpolation between closest ranks.</summary>
    public static decimal Percentile(IEnumerable<decimal> values, decimal p)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            throw new ArgumentException("The percentile of nothing is undefined.", nameof(values));
        }

        var rank = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (rank - lower));
    }

    /// <summary>
    /// The typical share of a month's spending done by day <paramref name="day"/>: the median, over
    /// past months, of (spent by that day ÷ month total). Rent and bills land early, so this beats
    /// a linear projection. Each month is its daily spending (index 0 = day 1); months with no
    /// spending are skipped. <c>null</c> without any month to learn from.
    /// </summary>
    public static decimal? ShareByDay(IEnumerable<IReadOnlyList<decimal>> pastMonthsDaily, int day)
    {
        var shares = new List<decimal>();
        foreach (var daily in pastMonthsDaily)
        {
            var total = daily.Sum();
            if (total <= 0)
            {
                continue;
            }

            shares.Add(daily.Take(Math.Min(day, daily.Count)).Sum() / total);
        }

        return shares.Count == 0 ? null : Median(shares);
    }

    /// <summary>
    /// Ranks insights: the robust z-score (capped) × ln of the amount at stake, so a big surprise in a
    /// small category and a small surprise in rent compete fairly.
    /// </summary>
    public static double Score(double z, decimal stake) => Math.Min(z, ZCap) * Math.Log(Math.Max((double)Math.Abs(stake), 1));
}
