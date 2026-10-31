using ExpenseInsights.Core.Insights;

namespace ExpenseInsights.Core.Tests.Insights;

/// <summary>The thresholds on hand-built series: steady, volatile, seasonal, with and without outliers.</summary>
public sealed class InsightStatisticsTests
{
    [Fact]
    public void Median_OfOddAndEvenCounts()
    {
        Assert.Equal(3m, InsightStatistics.Median([5, 1, 3]));
        Assert.Equal(2.5m, InsightStatistics.Median([4, 1, 3, 2]));
    }

    [Fact]
    public void Spread_IsTheScaledMad_AndIgnoresOneOutlier()
    {
        // Deviations from the median 11: 1, 0, 2, 1, 989 → MAD 1.
        Assert.Equal(1.4826m, InsightStatistics.Spread([10, 11, 9, 12, 1000]));
    }

    [Fact]
    public void Baseline_NeedsThreeMonths_AndUsesTheLastSix()
    {
        Assert.Null(InsightStatistics.Baseline([100, 200]));

        var baseline = InsightStatistics.Baseline([9000, 9000, 100, 200, 300, 400, 500, 600])!;

        Assert.Equal(6, baseline.Months);
        Assert.Equal(350m, baseline.Median);
    }

    [Fact]
    public void SteadySeries_FallsBackToTenPercent_AndTheFloor()
    {
        // Rent: no spread at all.
        var rent = InsightStatistics.Baseline([1500, 1500, 1500, 1500, 1500, 1500])!;
        Assert.Equal(0m, rent.Spread);

        Assert.False(InsightStatistics.Compare(1600, rent).IsUnusual); // 6.7%
        Assert.True(InsightStatistics.Compare(1650, rent).IsUnusual); // 10%

        // A small fixed bill: 10% is below the floor, so it takes 100 zł.
        var phone = InsightStatistics.Baseline([60, 60, 60, 60])!;
        Assert.False(InsightStatistics.Compare(120, phone).IsUnusual);
        Assert.True(InsightStatistics.Compare(160, phone).IsUnusual);
    }

    [Fact]
    public void VolatileSeries_NeedsABiggerJump()
    {
        // Restaurants: median 395, MAD 110 → spread 163.1, threshold 3 × 163.1 ≈ 489.
        var restaurants = InsightStatistics.Baseline([300, 520, 410, 260, 610, 380])!;

        var high = InsightStatistics.Compare(650, restaurants);
        var veryHigh = InsightStatistics.Compare(900, restaurants);

        Assert.False(high.IsUnusual);
        Assert.True(veryHigh.IsUnusual);
        Assert.True(veryHigh.IsUp);
        Assert.Equal(505m, veryHigh.Delta);
    }

    [Fact]
    public void AnOutlierMonth_DoesntHideTheNextChange()
    {
        // One 2000 zł month: the median and MAD barely move (a mean and standard deviation would).
        var baseline = InsightStatistics.Baseline([400, 410, 390, 2000, 405, 395])!;

        Assert.Equal(402.5m, baseline.Median);
        Assert.True(InsightStatistics.Compare(520, baseline).IsUnusual);
        Assert.False(InsightStatistics.Compare(480, baseline).IsUnusual);
    }

    [Fact]
    public void Drops_AreUnusualToo()
    {
        var groceries = InsightStatistics.Baseline([1500, 1450, 1550, 1500, 1480, 1520])!;

        var comparison = InsightStatistics.Compare(1100, groceries);

        Assert.True(comparison.IsUnusual);
        Assert.False(comparison.IsUp);
    }

    [Fact]
    public void SeasonalSpike_IsntNewsWhenEveryDecemberIsLikeThat()
    {
        // June–November around 1000; last December was 2500.
        var baseline = InsightStatistics.Baseline([1000, 980, 1020, 1010, 990, 1000])!;
        var lastDecember = InsightStatistics.SeasonalReference([2500]);

        Assert.True(InsightStatistics.Compare(2600, baseline).IsUnusual);
        Assert.False(InsightStatistics.CompareWithSeason(2600, baseline, lastDecember).IsUnusual);
        Assert.True(InsightStatistics.CompareWithSeason(4000, baseline, lastDecember).IsUnusual);
    }

    [Fact]
    public void SeasonalReference_IsLastYear_ThenTheMedianOfYears()
    {
        Assert.Null(InsightStatistics.SeasonalReference([]));
        Assert.Equal(2500m, InsightStatistics.SeasonalReference([2500]));
        Assert.Equal(2400m, InsightStatistics.SeasonalReference([2000, 2400, 9000]));
    }

    [Fact]
    public void Percentile_InterpolatesBetweenRanks()
    {
        decimal[] values = [10, 20, 30, 40, 50];

        Assert.Equal(48m, InsightStatistics.Percentile(values, 0.95m));
        Assert.Equal(50m, InsightStatistics.Percentile(values, 1m));
        Assert.Equal(30m, InsightStatistics.Percentile(values, 0.5m));
    }

    [Fact]
    public void ShareByDay_IsTheMedianProfile_SoEarlyBillsDontInflateTheProjection()
    {
        // Rent of 1000 on the 1st, then 50 a day for 20 days: 2000 a month.
        decimal[] month = [1050, .. Enumerable.Repeat(50m, 19), .. Enumerable.Repeat(0m, 10)];

        var share = InsightStatistics.ShareByDay([month, month, month], 10);

        Assert.Equal(0.75m, share); // 1500 of 2000 by day 10, not 10/30
        Assert.Null(InsightStatistics.ShareByDay([new decimal[30]], 10));
    }

    [Fact]
    public void Score_GrowsWithSurpriseAndStake_AndCapsZ()
    {
        Assert.True(InsightStatistics.Score(4, 500) > InsightStatistics.Score(4, 100));
        Assert.True(InsightStatistics.Score(6, 100) > InsightStatistics.Score(4, 100));
        Assert.Equal(InsightStatistics.Score(InsightStatistics.ZCap, 100), InsightStatistics.Score(1000, 100));
    }
}
