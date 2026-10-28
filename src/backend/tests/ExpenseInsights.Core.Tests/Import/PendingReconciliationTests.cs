using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;
using static ExpenseInsights.Core.Tests.Import.Fakes;

namespace ExpenseInsights.Core.Tests.Import;

public sealed class PendingReconciliationTests
{
    private static readonly DateOnly Day = new(2026, 10, 20);

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(-1, false)]
    public void Booked_MatchesAHoldUpToFiveDaysOld(int daysLater, bool matches)
    {
        var hold = Stored(-10.33m, Day);
        var booked = Imported(-10.33m, date: Day.AddDays(daysLater));

        Assert.Equal(matches, PendingReconciliation.FindPending(booked, [hold]) == hold);
    }

    [Fact]
    public void Booked_UsesThePurchaseDate()
    {
        var hold = Stored(-10.33m, Day);
        var booked = Imported(-10.33m, date: Day.AddDays(9), purchaseDate: Day);

        Assert.Same(hold, PendingReconciliation.FindPending(booked, [hold]));
    }

    [Fact]
    public void Booked_NeedsTheSameAmount_AndASimilarCounterparty()
    {
        var booked = Imported(-10.33m, date: Day, counterparty: "ZABKA Z5017 K.1 KRAKOW POL");

        Assert.Null(PendingReconciliation.FindPending(booked, [Stored(-10.34m, Day, "ZABKA Z5017 K.1 KRAKOW POL")]));
        Assert.Null(PendingReconciliation.FindPending(booked, [Stored(-10.33m, Day, "ORLEN STACJA NR 1234")]));
        Assert.NotNull(PendingReconciliation.FindPending(booked, [Stored(-10.33m, Day, "ZABKA Z5017 K.1 KRAKOW PO")]));
    }

    [Fact]
    public void Booked_PrefersTheClosestHold()
    {
        Transaction[] holds = [Stored(-10m, Day.AddDays(-3)), Stored(-10m, Day.AddDays(-1)), Stored(-10m, Day.AddDays(-2))];

        Assert.Same(holds[1], PendingReconciliation.FindPending(Imported(-10m, date: Day), holds));
    }

    [Fact]
    public void SameHold_MatchesOnAmountDateAndCounterparty()
    {
        var stored = Stored(-10m, Day);

        Assert.True(PendingReconciliation.IsSameHold(Imported(-10m, date: Day, purchaseDate: Day, status: TransactionStatus.Pending), stored));
        Assert.False(PendingReconciliation.IsSameHold(Imported(-10m, date: Day.AddDays(1), status: TransactionStatus.Pending), stored));
    }
}
