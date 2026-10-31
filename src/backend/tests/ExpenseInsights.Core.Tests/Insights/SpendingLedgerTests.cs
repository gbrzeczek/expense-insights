using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Insights;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Core.Tests.Insights;

public sealed class SpendingLedgerTests
{
    private static readonly DateOnly Day = new(2026, 10, 5);
    private static readonly Guid Coffee = Guid.NewGuid();

    private static readonly IReadOnlyList<Category> Categories =
    [
        .. FakeHistory.Categories,
        new Category { Id = Coffee, Name = "Kawa", ParentId = CategoryCatalog.Restaurants.Id, Kind = CategoryKind.Spending },
    ];

    [Fact]
    public void Spending_RollsUpToTheTopLevel_AndIsPositive()
    {
        var entry = Assert.Single(Build(Tx(-12.50m, Coffee)));

        Assert.Equal(Flow.Spending, entry.Flow);
        Assert.Equal(CategoryCatalog.Restaurants.Id, entry.CategoryId);
        Assert.Equal(12.50m, entry.Amount);
    }

    [Fact]
    public void OwnTransfers_ExpiredHolds_AndOtherCurrencies_DontCount()
    {
        var entries = Build(
            Tx(-2000, CategoryCatalog.OwnTransfer.Id),
            Tx(-2000, null, kind: TransactionKind.OwnTransfer),
            Tx(-30, CategoryCatalog.Groceries.Id, status: TransactionStatus.Expired),
            Tx(-30, CategoryCatalog.Groceries.Id, currency: "EUR"));

        Assert.Empty(entries);
    }

    [Fact]
    public void PendingHolds_Count()
    {
        Assert.Single(Build(Tx(-30, CategoryCatalog.Groceries.Id, status: TransactionStatus.Pending)));
    }

    [Fact]
    public void Income_IsIncome_AndUnclassifiedMoneyOutIsSpendingWithoutACategory()
    {
        var entries = Build(Tx(10000, CategoryCatalog.Salary.Id), Tx(-8.10m, CategoryCatalog.Interest.Id), Tx(-55, null), Tx(20, null));

        Assert.Equal(
            [(Flow.Income, CategoryCatalog.Salary.Id, 10000m), (Flow.Income, CategoryCatalog.Interest.Id, -8.10m), (Flow.Spending, null, 55m), (Flow.Income, null, 20m)],
            entries.Select(e => (e.Flow, e.CategoryId, e.Amount)));
    }

    [Fact]
    public void Refund_OffsetsTheCategoryTheMerchantIsUsuallyIn()
    {
        var entries = Build(
            Tx(-200, CategoryCatalog.Clothing.Id, merchant: "zalando"),
            Tx(-100, CategoryCatalog.Clothing.Id, merchant: "zalando"),
            Tx(-50, CategoryCatalog.Other.Id, merchant: "zalando"),
            Tx(80, CategoryCatalog.Refunds.Id, merchant: "zalando"),
            Tx(40, CategoryCatalog.Refunds.Id, merchant: "nowhere"));

        Assert.Equal((Flow.Spending, CategoryCatalog.Clothing.Id, -80m), (entries[3].Flow, entries[3].CategoryId, entries[3].Amount));
        Assert.Equal((CategoryCatalog.Other.Id, -40m), (entries[4].CategoryId, entries[4].Amount)); // no purchases to go by
    }

    [Fact]
    public void Reimbursement_OffsetsTheCategoryItPaysBack()
    {
        var entry = Assert.Single(Build(Tx(22, CategoryCatalog.TransfersFromPeople.Id, offsets: Coffee)));

        Assert.Equal((Flow.Spending, CategoryCatalog.Restaurants.Id, -22m), (entry.Flow, entry.CategoryId, entry.Amount));
    }

    [Fact]
    public void Splits_ReplaceTheTransactionsCategory()
    {
        var split = Tx(-150, CategoryCatalog.TransfersToPeople.Id) with
        {
            Splits = [new(CategoryCatalog.Restaurants.Id, -93), new(CategoryCatalog.Leisure.Id, -57)],
        };

        var entries = Build(split);

        Assert.Equal([(CategoryCatalog.Restaurants.Id, 93m), (CategoryCatalog.Leisure.Id, 57m)], entries.Select(e => (e.CategoryId!.Value, e.Amount)));
    }

    [Fact]
    public void FakeExport_MonthlyTotalsAddUp_AndOwnTransfersAreLeftOut()
    {
        var history = FakeHistory.SixMonths;
        var september = new Month(2026, 9);
        var transactions = FakeHistory.Transactions("fake_ing.labels.csv").Where(t => september.Contains(t.Date)).ToList();

        var byCategory = history.Spending(september).GroupBy(e => e.CategoryId).Sum(g => history.Spent(september, g.Key));
        var spending = -transactions.Where(t => t.Amount < 0 && t.Kind != TransactionKind.OwnTransfer && t.CategoryId != CategoryCatalog.Interest.Id).Sum(t => t.Amount)
            - transactions.Where(t => t.CategoryId == CategoryCatalog.Refunds.Id).Sum(t => t.Amount);

        Assert.Equal(history.Spent(september), byCategory);
        Assert.Equal(spending, history.Spent(september));
        Assert.Equal(-742.18m, -history.Spending(september).Single(e => e.MerchantKey == "czynsz").Amount);
        Assert.True(history.Income(september) > 9000);
    }

    private static List<LedgerEntry> Build(params LedgerTransaction[] transactions) =>
        [.. SpendingLedger.Build(transactions, Categories, "PLN")];

    private static LedgerTransaction Tx(
        decimal amount,
        Guid? category,
        string merchant = "shop",
        Guid? offsets = null,
        TransactionKind kind = TransactionKind.Card,
        TransactionStatus status = TransactionStatus.Booked,
        string currency = "PLN") =>
        new(Guid.NewGuid(), Day, amount, currency, kind, status, category, offsets, merchant, merchant, []);
}
