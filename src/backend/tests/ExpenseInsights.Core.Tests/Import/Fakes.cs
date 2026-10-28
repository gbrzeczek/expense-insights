using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Core.Tests.Import;

internal static class Fakes
{
    public static ImportedTransaction Imported(
        decimal amount,
        decimal? balance = null,
        string account = "Main",
        string counterparty = "LIDL KAMIENSKIEGO KRAKOW POL",
        DateOnly? date = null,
        DateOnly? purchaseDate = null,
        TransactionStatus status = TransactionStatus.Booked,
        int line = 1) => new()
    {
        BankId = "test",
        AccountRef = account,
        ExternalId = status == TransactionStatus.Booked ? $"id-{line}" : null,
        Status = status,
        TransactionDate = date ?? new DateOnly(2026, 10, 1),
        PurchaseDate = purchaseDate,
        Amount = amount,
        Currency = "PLN",
        Kind = TransactionKind.Card,
        CounterpartyRaw = counterparty,
        TitleRaw = "",
        CounterpartyName = counterparty,
        BalanceAfter = balance,
        RawRow = new RawRow(line, []),
    };

    public static Transaction Stored(decimal amount, DateOnly date, string counterparty = "LIDL KAMIENSKIEGO KRAKOW POL") => new()
    {
        Id = Guid.NewGuid(),
        Status = TransactionStatus.Pending,
        TransactionDate = date,
        PurchaseDate = date,
        Amount = amount,
        Currency = "PLN",
        CounterpartyRaw = counterparty,
        TitleRaw = "",
        CounterpartyName = counterparty,
        RawRow = new RawRow(1, []),
    };
}
