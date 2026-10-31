using ExpenseInsights.Core.Insights;
using static ExpenseInsights.Core.Tests.Insights.Ledger;

namespace ExpenseInsights.Core.Tests.Insights;

public sealed class RecurringPaymentsTests
{
    [Fact]
    public void Monthly_SameAmount_IsDetected_WithItsCosts()
    {
        var ledger = new Ledger().Monthly(M(2026, 5), M(2026, 10), 5, 49m, Subscriptions, "netflix");

        var netflix = Assert.Single(RecurringPayments.Detect(ledger.Entries, D(2026, 10, 28)));

        Assert.Equal(30, netflix.CadenceDays);
        Assert.Equal(6, netflix.Occurrences);
        Assert.Equal(D(2026, 11, 5), netflix.NextDate);
        Assert.Equal(49m, netflix.MonthlyCost);
        Assert.Equal(588m, netflix.YearlyCost);
        Assert.True(netflix.IsActive);
        Assert.False(netflix.PriceChanged);
        Assert.Equal(Subscriptions, netflix.CategoryId);
    }

    [Fact]
    public void Weekly_AndYearly_CadencesAreDetected()
    {
        var ledger = new Ledger();
        for (var day = D(2026, 9, 1); day <= D(2026, 10, 27); day = day.AddDays(7))
        {
            ledger.Spend(day, 30, Leisure, "gym");
        }

        ledger.Spend(D(2024, 3, 2), 99, Subscriptions, "domain").Spend(D(2025, 3, 3), 99, Subscriptions, "domain").Spend(D(2026, 3, 1), 105, Subscriptions, "domain");

        var found = RecurringPayments.Detect(ledger.Entries, D(2026, 10, 28)).ToDictionary(r => r.MerchantKey);

        Assert.Equal(7, found["gym"].CadenceDays);
        Assert.Equal(130m, found["gym"].MonthlyCost); // 30 × 52 ÷ 12
        Assert.Equal(365, found["domain"].CadenceDays);
        Assert.Equal(8.75m, found["domain"].MonthlyCost);
        Assert.Equal(105m, found["domain"].YearlyCost);
    }

    [Fact]
    public void TwoPayments_AreEnough_WithinTenPercent()
    {
        var ledger = new Ledger().Spend(D(2026, 9, 12), 23.99m, Subscriptions, "spotify").Spend(D(2026, 10, 12), 25.50m, Subscriptions, "spotify");

        Assert.Single(RecurringPayments.Detect(ledger.Entries, D(2026, 10, 28)));
    }

    [Fact]
    public void Shops_AndVaryingBills_AreNotSubscriptions()
    {
        var ledger = new Ledger();
        decimal[] amounts = [118, 205, 161, 232, 140, 190, 125, 215];
        for (var i = 0; i < amounts.Length; i++)
        {
            // An electricity bill on the 20th, and a shop visited weekly with random baskets.
            ledger.Spend(new Month(2026, 3).Plus(i).First.AddDays(19), amounts[i], Bills, "tauron");
            ledger.Spend(D(2026, 8, 1).AddDays(7 * i), amounts[^(i + 1)] / 2, merchant: "lidl");
        }

        // Two matching baskets in a row at the end don't make a subscription.
        ledger.Spend(D(2026, 9, 30), 40, merchant: "lidl").Spend(D(2026, 10, 7), 41, merchant: "lidl");

        Assert.Empty(RecurringPayments.Detect(ledger.Entries, D(2026, 10, 28)));
    }

    [Fact]
    public void FrequentCoffee_AtSimilarPrices_IsNotASubscription_ButNetflixAndSpotifyAre()
    {
        var ledger = new Ledger()
            .Monthly(M(2026, 5), M(2026, 10), 5, 49m, Subscriptions, "netflix")
            .Monthly(M(2026, 5), M(2026, 10), 12, 23.99m, Subscriptions, "spotify");
        decimal[] prices = [14.50m, 14.90m, 15.50m, 15.90m];
        int[] gaps = [6, 8, 5, 9, 7, 7, 4, 10, 6, 8, 7, 3, 11, 7, 6, 8];
        var day = D(2026, 5, 4);
        for (var i = 0; i < 24; i++)
        {
            // Costa most weeks: a median gap of 7 days at a price within 10%, but not on schedule.
            ledger.Spend(day, prices[i % prices.Length], Restaurants, "costacoffee");
            day = day.AddDays(gaps[i % gaps.Length]);
        }

        Assert.Equal(["netflix", "spotify"], RecurringPayments.Detect(ledger.Entries, D(2026, 10, 28)).Select(r => r.MerchantKey));
    }

    [Fact]
    public void Coffee_TwoAtANewPriceAMonthApart_IsNotASubscription()
    {
        // Weekly coffee at the old price, then two at a new one a month apart: the merchant was paid
        // more often than monthly, so the last two are a coincidence.
        var ledger = new Ledger();
        for (var day = D(2026, 6, 1); day <= D(2026, 8, 31); day = day.AddDays(4))
        {
            ledger.Spend(day, 11m, null, "costacoffee");
        }

        ledger.Spend(D(2026, 9, 20), 15m, null, "costacoffee").Spend(D(2026, 10, 20), 15m, null, "costacoffee");

        Assert.Empty(RecurringPayments.Detect(ledger.Entries, D(2026, 10, 28)));
    }

    [Fact]
    public void EverydayCategories_NeedFourPaymentsAtTheSamePrice()
    {
        var ledger = new Ledger()
            // Lunch on three Fridays in a row is a habit, not a subscription.
            .Spend(D(2026, 10, 9), 32m, Restaurants, "bistro").Spend(D(2026, 10, 16), 32m, Restaurants, "bistro").Spend(D(2026, 10, 23), 32m, Restaurants, "bistro")
            // A weekly basket on schedule but at varying prices neither.
            .Spend(D(2026, 9, 7), 100m, Groceries, "farmbox").Spend(D(2026, 9, 14), 104m, Groceries, "farmbox")
            .Spend(D(2026, 9, 21), 97m, Groceries, "farmbox").Spend(D(2026, 9, 28), 101m, Groceries, "farmbox");
        // A meal-kit plan: the same price every month.
        ledger.Monthly(M(2026, 6), M(2026, 10), 3, 259m, Restaurants, "mealkit");

        var found = RecurringPayments.Detect(ledger.Entries, D(2026, 10, 28));

        Assert.Equal(["mealkit"], found.Select(r => r.MerchantKey));
    }

    [Fact]
    public void Monthly_WithAGapOffSchedule_IsNotASubscription()
    {
        // Median 30 days, but one payment came after 12 days and the next after 48.
        var ledger = new Ledger()
            .Spend(D(2026, 5, 5), 40m, Bills, "carwash").Spend(D(2026, 6, 4), 40m, Bills, "carwash")
            .Spend(D(2026, 7, 4), 40m, Bills, "carwash").Spend(D(2026, 7, 16), 40m, Bills, "carwash")
            .Spend(D(2026, 9, 2), 40m, Bills, "carwash").Spend(D(2026, 10, 2), 40m, Bills, "carwash");

        Assert.Empty(RecurringPayments.Detect(ledger.Entries, D(2026, 10, 28)));
    }

    [Fact]
    public void PriceChange_OnSchedule_IsTheSameSubscription()
    {
        var ledger = new Ledger().Monthly(M(2026, 5), M(2026, 9), 5, 49m, Subscriptions, "netflix").Spend(D(2026, 10, 5), 55m, Subscriptions, "netflix");

        var netflix = Assert.Single(RecurringPayments.Detect(ledger.Entries, D(2026, 10, 28)));

        Assert.True(netflix.PriceChanged);
        Assert.Equal((55m, 49m), (netflix.LatestAmount, netflix.PreviousAmount));

        // A month later the new price is just the price, counted from when it started.
        ledger.Spend(D(2026, 11, 5), 55m, Subscriptions, "netflix");
        var later = Assert.Single(RecurringPayments.Detect(ledger.Entries, D(2026, 11, 28)));
        Assert.False(later.PriceChanged);
        Assert.Equal(D(2026, 10, 5), later.FirstDate);
    }

    [Fact]
    public void Stopped_IsInactive()
    {
        var ledger = new Ledger().Monthly(M(2026, 1), M(2026, 6), 5, 49m, merchant: "netflix");

        Assert.False(Assert.Single(RecurringPayments.Detect(ledger.Entries, D(2026, 10, 28))).IsActive);
    }

    [Fact]
    public void FakeExport_FindsTheGeneratorsSubscriptionsAndRent_AndNothingElse()
    {
        var found = RecurringPayments.Detect(FakeHistory.SixMonths.Entries, FakeHistory.SixMonths.DataEnd);

        Assert.Equal(
            [("czynsz", 742.18m), ("netflix", 49m), ("spotify", 23.99m), ("youtubepremium", 29.99m)],
            found.Select(r => (r.MerchantKey, r.LatestAmount)));
        Assert.All(found, r => Assert.Equal(30, r.CadenceDays));
        Assert.All(found, r => Assert.True(r.IsActive));
    }

    [Fact]
    public void FakeHistory_TwoYears_StillOnlyTheSubscriptionsAndRent()
    {
        var found = RecurringPayments.Detect(FakeHistory.TwoYears.Entries, FakeHistory.TwoYears.DataEnd);

        Assert.Equal(["czynsz", "netflix", "spotify", "youtubepremium"], found.Select(r => r.MerchantKey));
    }
}
