using ExpenseInsights.Core.Import;

namespace ExpenseInsights.Core.Tests.Import;

public sealed class OwnTransferPairingTests
{
    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid Savings = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 11);

    [Fact]
    public void Pairs_DebitAndMatchingCredit()
    {
        var debit = new OwnTransferLeg(Guid.NewGuid(), Main, Savings, -2000m, Day);
        var credit = new OwnTransferLeg(Guid.NewGuid(), Savings, Main, 2000m, Day.AddDays(1));

        var pairs = OwnTransferPairing.Pair([credit, debit]);

        Assert.Equal([(debit.Id, credit.Id)], pairs);
    }

    [Fact]
    public void DoesNotPair_DifferentAmounts_WrongAccount_OrTooFarApart()
    {
        var debit = new OwnTransferLeg(Guid.NewGuid(), Main, Savings, -2000m, Day);

        Assert.Empty(OwnTransferPairing.Pair([debit, new OwnTransferLeg(Guid.NewGuid(), Savings, Main, 1999m, Day)]));
        Assert.Empty(OwnTransferPairing.Pair([debit, new OwnTransferLeg(Guid.NewGuid(), Main, Savings, 2000m, Day)]));
        Assert.Empty(OwnTransferPairing.Pair([debit, new OwnTransferLeg(Guid.NewGuid(), Savings, Main, 2000m, Day.AddDays(2))]));
    }

    [Fact]
    public void EachCredit_IsUsedOnce()
    {
        var first = new OwnTransferLeg(Guid.NewGuid(), Main, Savings, -2000m, Day);
        var second = new OwnTransferLeg(Guid.NewGuid(), Main, Savings, -2000m, Day.AddMonths(1));
        var credit = new OwnTransferLeg(Guid.NewGuid(), Savings, Main, 2000m, Day);

        Assert.Equal([(first.Id, credit.Id)], OwnTransferPairing.Pair([first, second, credit]));
    }
}
