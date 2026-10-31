using System.Globalization;
using System.Text;
using System.Text.Json;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Insights;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Core.Tests.Insights;

/// <summary>
/// The generated fake exports as a ledger, from their labels files: the true category of every row,
/// so the insight tests don't depend on the classifier.
/// </summary>
internal static class FakeHistory
{
    public static IReadOnlyList<Category> Categories { get; } =
        [.. CategoryCatalog.All.Select(c => new Category { Id = c.Id, Key = c.Key, Kind = c.Kind, SortOrder = c.SortOrder })];

    /// <summary><c>test-data/fake_ing.csv</c>: 6 months, its preamble says 2026-05-01 – 2026-10-28.</summary>
    public static SpendingHistory SixMonths { get; } = Load("fake_ing.labels.csv", new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 28));

    /// <summary><c>test-data/fake_ing_history.csv</c>: 25 months, 2024-10-08 – 2026-10-28.</summary>
    public static SpendingHistory TwoYears { get; } = Load("fake_ing_history.labels.csv", new DateOnly(2024, 10, 8), new DateOnly(2026, 10, 28));

    public static IReadOnlyList<LedgerTransaction> Transactions(string labels)
    {
        var names = CategoryNames();
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "test-data", labels));
        return [.. lines.Skip(1).Where(l => l.Length > 0).Select(line =>
        {
            // row;tx_date;tx_id;amount;account;kind;status;merchant;category
            var f = line.Split(';');
            var row = int.Parse(f[0], CultureInfo.InvariantCulture);
            return new LedgerTransaction(
                new Guid(row, 0, 0, new byte[8]),
                DateOnly.ParseExact(f[1], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                decimal.Parse(f[3].Replace(',', '.'), CultureInfo.InvariantCulture),
                "PLN",
                f[5] == "own_transfer" ? TransactionKind.OwnTransfer : TransactionKind.Other,
                f[6] == "pending" ? TransactionStatus.Pending : TransactionStatus.Booked,
                names[f[8]],
                null,
                Key(f[7]),
                f[7],
                []);
        })];
    }

    public static SpendingHistory Load(string labels, DateOnly start, DateOnly end) =>
        new(SpendingLedger.Build(Transactions(labels), Categories, "PLN"), SpendingHistory.FirstFullMonth(start), end);

    /// <summary>A merchant key like the normalizer's: lower case without diacritics.</summary>
    private static string Key(string merchant)
    {
        var folded = merchant.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD);
        return new string([.. folded.Where(c => char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);
    }

    private static Dictionary<string, Guid> CategoryNames()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "i18n", "pl.json")));
        return document.RootElement.GetProperty("categories").EnumerateObject()
            .Where(e => CategoryCatalog.TryGetByKey(e.Name, out _))
            .ToDictionary(e => e.Value.GetString()!, e => CategoryCatalog.ByKey(e.Name).Id);
    }
}
