using ExpenseInsights.Core.Insights;
using static ExpenseInsights.Core.Tests.Insights.Ledger;

namespace ExpenseInsights.Core.Tests.Insights;

public sealed class InsightRuleTests
{
    private static readonly Month October = M(2026, 10);

    [Fact]
    public void CategoryChange_FlagsASpikeAgainstTheBaseline()
    {
        var ledger = new Ledger()
            .Monthly(M(2026, 4), M(2026, 9), 10, 400, Restaurants)
            .Spend(D(2026, 10, 10), 900, Restaurants);
        var context = InsightContext.For(ledger.History(M(2026, 4), D(2026, 10, 31)), October);

        var insight = Assert.Single(new CategoryChangeRule().Evaluate(context));

        Assert.Equal((InsightType.CategoryChange, InsightDirection.Up, Restaurants), (insight.Type, insight.Direction, insight.CategoryId));
        Assert.Equal((900m, 400m, 6), (insight.Amount, insight.Usual, insight.Months));
        Assert.Equal(1.25m, insight.Change);
    }

    [Fact]
    public void CategoryChange_NeedsThreeMonths_AndReportsNoDropBeforeTheMonthEnds()
    {
        var ledger = new Ledger().Monthly(M(2026, 4), M(2026, 9), 10, 400, Restaurants);

        var young = InsightContext.For(ledger.History(M(2026, 8), D(2026, 10, 31)), October);
        var partial = InsightContext.For(ledger.History(M(2026, 4), D(2026, 10, 15)), October);
        var complete = InsightContext.For(ledger.History(M(2026, 4), D(2026, 10, 31)), October);

        Assert.Empty(new CategoryChangeRule().Evaluate(young));
        Assert.Empty(new CategoryChangeRule().Evaluate(partial));
        Assert.Equal(InsightDirection.Down, Assert.Single(new CategoryChangeRule().Evaluate(complete)).Direction);
    }

    [Fact]
    public void CategoryChange_SkipsWhatTheSeasonExplains()
    {
        // Gifts every December: 1000 a month, 2500 last December, 2600 this one.
        var ledger = new Ledger().Monthly(M(2025, 1), M(2026, 11), 10, 1000).Spend(D(2025, 12, 12), 1500).Spend(D(2026, 12, 12), 1600);
        var history = ledger.History(M(2025, 1), D(2026, 12, 31));

        Assert.Empty(new CategoryChangeRule().Evaluate(InsightContext.For(history, M(2026, 12))));

        ledger.Spend(D(2026, 12, 20), 1500);
        var spike = Assert.Single(new CategoryChangeRule().Evaluate(InsightContext.For(ledger.History(M(2025, 1), D(2026, 12, 31)), M(2026, 12))));
        Assert.Equal(2500m, spike.LastYear);
    }

    [Fact]
    public void Pace_ProjectsFromTheDayOfMonthProfile_AfterDaySeven()
    {
        // Rent of 2000 on the 1st and 50 a day: 3500 a month, 3000 of it by the 20th. This October
        // it is 100 a day: 4000 by the 20th, so 4000 ÷ (3000/3500) for the month, not 4000 × 31/20.
        var ledger = new Ledger();
        for (var month = M(2026, 4); month <= October; month = month.Plus(1))
        {
            ledger.Spend(month.First, 2000, Bills, "rent");
            for (var day = 1; day <= 30 && day <= month.Days && !(month == October && day > 20); day++)
            {
                ledger.Spend(new DateOnly(month.Year, month.Number, day), month == October && day <= 20 ? 100 : 50);
            }
        }

        var history = ledger.History(M(2026, 4), D(2026, 10, 20));
        var pace = PaceRule.Project(history, October)!;
        var insight = Assert.Single(new PaceRule().Evaluate(InsightContext.For(history, October)));

        Assert.Equal(4000m, pace.SoFar);
        Assert.Equal(3000m, pace.UsualByNow);
        Assert.Equal(InsightType.Pace, insight.Type);
        Assert.Equal(4000m, insight.SoFar);
        Assert.Equal(4666.67m, insight.Amount);
        Assert.Empty(new PaceRule().Evaluate(InsightContext.For(ledger.History(M(2026, 4), D(2026, 10, 7)), October)));
        Assert.Null(PaceRule.Project(ledger.History(M(2026, 4), D(2026, 10, 31)), October));
    }

    [Fact]
    public void LargePayment_AboveTheYearsLargest_AndThreeTimesTheMedian()
    {
        var ledger = new Ledger();
        for (var month = M(2025, 10); month < October; month = month.Plus(1))
        {
            ledger.Spend(month.First.AddDays(3), 120, Electronics, "media expert").Spend(month.First.AddDays(9), 60, Electronics, "allegro");
        }

        var tv = Guid.NewGuid();
        ledger.Spend(D(2026, 10, 3), 1900, Electronics, "media expert", tv).Spend(D(2026, 10, 9), 60, Electronics, "allegro");
        var context = InsightContext.For(ledger.History(M(2025, 10), D(2026, 10, 31)), October);

        var insight = Assert.Single(new LargePaymentRule().Evaluate(context));

        Assert.Equal((tv, 1900m, 90m), (insight.TransactionId!.Value, insight.Amount, insight.Usual));

        // A category whose payments vary that much anyway doesn't flag its largest.
        var lumpy = new Ledger();
        for (var month = M(2025, 10); month < October; month = month.Plus(1))
        {
            lumpy.Spend(month.First.AddDays(3), month.Number % 2 == 0 ? 2000 : 60, Electronics);
        }

        lumpy.Spend(D(2026, 10, 3), 1900, Electronics);
        Assert.Empty(new LargePaymentRule().Evaluate(InsightContext.For(lumpy.History(M(2025, 10), D(2026, 10, 31)), October)));
    }

    [Fact]
    public void FrequentSmall_CountAndSumUpAtOneMerchant()
    {
        var ledger = new Ledger();
        for (var month = M(2026, 4); month <= October; month = month.Plus(1))
        {
            var visits = month == October ? 14 : 5;
            for (var i = 0; i < visits; i++)
            {
                ledger.Spend(month.First.AddDays(i * 2), 24, merchant: "zabka");
            }
        }

        var insight = Assert.Single(new FrequentSmallRule().Evaluate(InsightContext.For(ledger.History(M(2026, 4), D(2026, 10, 31)), October)));

        Assert.Equal(("zabka", 14, 5), (insight.MerchantKey, insight.Count, insight.UsualCount));
        Assert.Equal((336m, 120m), (insight.Amount, insight.Usual));
    }

    [Fact]
    public void Subscription_NewOnlyAfterTheHistoryStarts_AndPriceChanges()
    {
        var ledger = new Ledger()
            .Monthly(M(2026, 4), October, 5, 49, Subscriptions, "netflix")
            .Monthly(M(2026, 9), October, 12, 23.99m, Subscriptions, "spotify")
            .Monthly(M(2026, 4), M(2026, 9), 18, 29.99m, Subscriptions, "youtube")
            .Spend(D(2026, 10, 18), 33.99m, Subscriptions, "youtube");
        var history = ledger.History(M(2026, 4), D(2026, 10, 31));

        var insights = new SubscriptionRule().Evaluate(InsightContext.For(history, October)).ToDictionary(i => i.MerchantKey!);
        var april = new SubscriptionRule().Evaluate(InsightContext.For(history, M(2026, 5)));

        Assert.Equal(["spotify", "youtube"], insights.Keys.Order());
        Assert.Equal((InsightType.NewSubscription, 287.88m), (insights["spotify"].Type, insights["spotify"].Yearly));
        Assert.Equal((InsightType.SubscriptionPriceChange, 33.99m, 29.99m), (insights["youtube"].Type, insights["youtube"].Amount, insights["youtube"].Usual));
        Assert.Empty(april); // Netflix was there when the imports start: not news
    }

    [Fact]
    public void Uncategorized_CountsTheMonthsOpenPayments()
    {
        var ledger = new Ledger().Unclassified(D(2026, 10, 3), 40).Unclassified(D(2026, 10, 9), 60).Unclassified(D(2026, 9, 9), 60);

        var insight = Assert.Single(new UncategorizedRule().Evaluate(InsightContext.For(ledger.History(M(2026, 9), D(2026, 10, 31)), October)));

        Assert.Equal((2, 100m), (insight.Count, insight.Amount));
    }

    [Fact]
    public void Engine_RanksTopThree_OneStoryPerCategory_ThenTheReminder()
    {
        var ledger = new Ledger();
        Guid[] categories = [Groceries, Restaurants, Bills, Electronics, Subscriptions];
        for (var i = 0; i < categories.Length; i++)
        {
            ledger.Monthly(M(2026, 4), M(2026, 9), 3, 100 * (i + 1), categories[i]);
            ledger.Spend(D(2026, 10, 3), 100 * (i + 1) * 4, categories[i]);
        }

        ledger.Unclassified(D(2026, 10, 3), 10);
        var context = InsightContext.For(ledger.History(M(2026, 4), D(2026, 10, 31)), October);

        var insights = InsightEngine.Default.Evaluate(context);

        Assert.Equal(4, insights.Count);
        Assert.Equal([Subscriptions, Electronics, Bills], insights.Take(3).Select(i => i.CategoryId!.Value));
        Assert.Equal(InsightType.Uncategorized, insights[3].Type);
        Assert.Equal(insights.Take(3).OrderByDescending(i => i.Score), insights.Take(3));
    }

    [Fact]
    public void Engine_QuietMonth_HasNothing()
    {
        var ledger = new Ledger().Monthly(M(2026, 4), October, 3, 500, Groceries).Monthly(M(2026, 4), October, 1, 1500, Bills, "rent");

        Assert.Empty(InsightEngine.Default.Evaluate(InsightContext.For(ledger.History(M(2026, 4), D(2026, 10, 31)), October)));
    }

    [Fact]
    public void FakeHistory_ANormalMonthShowsZeroToTwoInsights()
    {
        var history = FakeHistory.TwoYears;
        var counts = new List<int>();
        for (var month = history.FirstMonth; month <= history.LastMonth; month = month.Plus(1))
        {
            var insights = InsightEngine.Default.Evaluate(InsightContext.For(history, month));
            Assert.InRange(insights.Count, 0, InsightEngine.MaxRanked); // the labels have no unclassified rows
            Assert.DoesNotContain(insights, i => i.Type == InsightType.NewSubscription);
            counts.Add(insights.Count);
        }

        // Random data with no real events: mostly quiet, never a wall of cards.
        Assert.True(counts.Average() <= 2, $"{counts.Average():0.0} insights a month on average");
        Assert.Contains(0, counts);
    }

    [Fact]
    public void FakeExport_FirstMonthsHaveNoBaseline_TheLastIsInProgress()
    {
        var history = FakeHistory.SixMonths;

        Assert.Empty(InsightEngine.Default.Evaluate(InsightContext.For(history, M(2026, 5))));
        Assert.Empty(InsightEngine.Default.Evaluate(InsightContext.For(history, M(2026, 7))));
        var pace = PaceRule.Project(history, October)!;
        Assert.Equal(28, pace.Day);
        Assert.Equal(history.Spent(October), pace.SoFar);
        Assert.True(pace.Projected >= pace.SoFar);
    }
}
