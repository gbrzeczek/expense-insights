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
            ledger.Spend(day, 30, merchant: "gym");
        }

        ledger.Spend(D(2024, 3, 2), 99, merchant: "domain").Spend(D(2025, 3, 3), 99, merchant: "domain").Spend(D(2026, 3, 1), 105, merchant: "domain");

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
        var ledger = new Ledger().Spend(D(2026, 9, 12), 23.99m, merchant: "spotify").Spend(D(2026, 10, 12), 25.50m, merchant: "spotify");

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
