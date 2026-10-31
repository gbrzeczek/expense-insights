using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Insights;

namespace ExpenseInsights.Core.Tests.Insights;

/// <summary>Hand-built ledgers for the rule tests.</summary>
internal sealed class Ledger
{
    public static readonly Guid Groceries = CategoryCatalog.Groceries.Id;
    public static readonly Guid Restaurants = CategoryCatalog.Restaurants.Id;
    public static readonly Guid Bills = CategoryCatalog.Bills.Id;
    public static readonly Guid Electronics = CategoryCatalog.Electronics.Id;
    public static readonly Guid Subscriptions = CategoryCatalog.Subscriptions.Id;

    private readonly List<LedgerEntry> _entries = [];

    public Ledger Spend(DateOnly date, decimal amount, Guid? category = null, string merchant = "shop", Guid? id = null)
    {
        _entries.Add(new LedgerEntry(id ?? Guid.NewGuid(), date, Flow.Spending, category ?? Groceries, amount, merchant, merchant));
        return this;
    }

    public Ledger Unclassified(DateOnly date, decimal amount)
    {
        _entries.Add(new LedgerEntry(Guid.NewGuid(), date, Flow.Spending, null, amount, "unknown", "Unknown"));
        return this;
    }

    /// <summary>The same amount on the same day of every month from <paramref name="from"/> through <paramref name="to"/>.</summary>
    public Ledger Monthly(Month from, Month to, int day, decimal amount, Guid? category = null, string merchant = "shop")
    {
        for (var month = from; month <= to; month = month.Plus(1))
        {
            Spend(new DateOnly(month.Year, month.Number, Math.Min(day, month.Days)), amount, category, merchant);
        }

        return this;
    }

    public List<LedgerEntry> Entries => _entries;

    /// <summary>Imported from the 1st of <paramref name="first"/> to <paramref name="end"/>.</summary>
    public SpendingHistory History(Month first, DateOnly end) => new(_entries, first, end);

    public static DateOnly D(int year, int month, int day) => new(year, month, day);

    public static Month M(int year, int month) => new(year, month);
}
