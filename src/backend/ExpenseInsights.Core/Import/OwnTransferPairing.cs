namespace ExpenseInsights.Core.Import;

/// <summary>
/// One leg of a possible own transfer. <c>CounterpartyAccountId</c> is the user's own account the
/// money went to or came from, if it is known.
/// </summary>
public sealed record OwnTransferLeg(Guid Id, Guid AccountId, Guid? CounterpartyAccountId, decimal Amount, DateOnly Date);

/// <summary>
/// Links the two legs of a transfer between the user's own accounts: a debit on A to B and the
/// matching credit on B (same amount, same date ± 1 day).
/// </summary>
public static class OwnTransferPairing
{
    public const int MaxDaysApart = 1;

    public static IReadOnlyList<(Guid DebitId, Guid CreditId)> Pair(IReadOnlyCollection<OwnTransferLeg> legs)
    {
        var pairs = new List<(Guid, Guid)>();
        var usedCredits = new HashSet<Guid>();

        foreach (var debit in legs.Where(l => l.Amount < 0 && l.CounterpartyAccountId is not null).OrderBy(l => l.Date))
        {
            var credit = legs
                .Where(c => c.Amount == -debit.Amount
                    && c.AccountId == debit.CounterpartyAccountId
                    && (c.CounterpartyAccountId is null || c.CounterpartyAccountId == debit.AccountId)
                    && Math.Abs(c.Date.DayNumber - debit.Date.DayNumber) <= MaxDaysApart
                    && !usedCredits.Contains(c.Id))
                .OrderBy(c => Math.Abs(c.Date.DayNumber - debit.Date.DayNumber))
                .FirstOrDefault();
            if (credit is not null)
            {
                usedCredits.Add(credit.Id);
                pairs.Add((debit.Id, credit.Id));
            }
        }

        return pairs;
    }
}
