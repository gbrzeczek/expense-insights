namespace ExpenseInsights.Core.Classification;

/// <summary>A category of the global taxonomy. Its display name is the i18n key <c>categories.{Key}</c>.</summary>
public sealed record CatalogCategory(Guid Id, string Key, CategoryKind Kind, int SortOrder);

/// <summary>
/// The global, two-level taxonomy (plan 04): fixed top-level categories with stable ids, seeded by
/// a migration. Users rename, hide and add subcategories, but don't change what these mean.
/// </summary>
public static class CategoryCatalog
{
    public static readonly CatalogCategory Groceries = New(1, "groceries", CategoryKind.Spending);
    public static readonly CatalogCategory Restaurants = New(2, "restaurants", CategoryKind.Spending);
    public static readonly CatalogCategory Transport = New(3, "transport", CategoryKind.Spending);
    public static readonly CatalogCategory Health = New(4, "health", CategoryKind.Spending);
    public static readonly CatalogCategory Beauty = New(5, "beauty", CategoryKind.Spending);
    public static readonly CatalogCategory Home = New(6, "home", CategoryKind.Spending);
    public static readonly CatalogCategory Clothing = New(7, "clothing", CategoryKind.Spending);
    public static readonly CatalogCategory Electronics = New(8, "electronics", CategoryKind.Spending);
    public static readonly CatalogCategory Leisure = New(9, "leisure", CategoryKind.Spending);
    public static readonly CatalogCategory Subscriptions = New(10, "subscriptions", CategoryKind.Spending);
    public static readonly CatalogCategory Bills = New(11, "bills", CategoryKind.Spending);
    public static readonly CatalogCategory OnlineShopping = New(12, "onlineShopping", CategoryKind.Spending);

    /// <summary>The holding category for outgoing transfers to people (decision D11).</summary>
    public static readonly CatalogCategory TransfersToPeople = New(13, "transfersToPeople", CategoryKind.Spending);
    public static readonly CatalogCategory Other = New(14, "other", CategoryKind.Spending);
    public static readonly CatalogCategory Salary = New(15, "salary", CategoryKind.Income);
    public static readonly CatalogCategory TransfersFromPeople = New(16, "transfersFromPeople", CategoryKind.Income);

    /// <summary>Other income, e.g. a transfer from the user's own business account (not in the export).</summary>
    public static readonly CatalogCategory OtherIncome = New(17, "otherIncome", CategoryKind.Income);

    /// <summary>Savings interest, net of the interest tax row.</summary>
    public static readonly CatalogCategory Interest = New(18, "interest", CategoryKind.Income);
    public static readonly CatalogCategory Refunds = New(19, "refunds", CategoryKind.Refund);

    /// <summary>Between the user's own accounts in the export: excluded from spending and income.</summary>
    public static readonly CatalogCategory OwnTransfer = New(20, "ownTransfer", CategoryKind.Excluded);

    /// <summary>Money into and out of investments (a broker, treasury bonds): still the user's, so excluded like own transfers.</summary>
    public static readonly CatalogCategory Investments = New(21, "investments", CategoryKind.Excluded);

    public static readonly IReadOnlyList<CatalogCategory> All =
    [
        Groceries, Restaurants, Transport, Health, Beauty, Home, Clothing, Electronics, Leisure, Subscriptions,
        Bills, OnlineShopping, TransfersToPeople, Other, Salary, TransfersFromPeople, OtherIncome, Interest,
        Refunds, OwnTransfer, Investments,
    ];

    private static readonly Dictionary<string, CatalogCategory> ByKeyMap = All.ToDictionary(c => c.Key, StringComparer.Ordinal);

    public static CatalogCategory ByKey(string key) =>
        ByKeyMap.TryGetValue(key, out var category) ? category : throw new KeyNotFoundException($"No category '{key}'.");

    public static bool TryGetByKey(string key, out CatalogCategory category) => ByKeyMap.TryGetValue(key, out category!);

    private static CatalogCategory New(int number, string key, CategoryKind kind) =>
        new(new Guid($"0000000c-0000-0000-0000-{number:D12}"), key, kind, number * 10);
}
