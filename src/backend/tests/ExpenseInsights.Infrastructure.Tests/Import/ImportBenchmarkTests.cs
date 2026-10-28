using System.Diagnostics;
using System.Globalization;
using ExpenseInsights.Testing;

namespace ExpenseInsights.Infrastructure.Tests.Import;

/// <summary>
/// Plan 02's speed targets (measured on the i3 home server). Explicit, so CI skips them; run with
/// <c>dotnet test --project tests/ExpenseInsights.Infrastructure.Tests -- --explicit only</c>.
/// </summary>
public sealed class ImportBenchmarkTests(PostgresFixture postgres) : ImportTestBase(postgres)
{
    [Theory(Explicit = true)]
    [InlineData(2_600, 2)] // a real 28-month export
    [InlineData(10_000, 5)]
    public async Task Import_IsFastEnough(int rows, int maxSeconds)
    {
        var user = await NewUserAsync();
        var file = Encode(Export(rows, idPrefix: "1"));
        await ImportAsync(await NewUserAsync(), Encode(Export(50, idPrefix: "9"))); // warm-up

        var stopwatch = Stopwatch.StartNew();
        var (batch, _) = await ImportAsync(user, file);
        stopwatch.Stop();

        TestContext.Current.SendDiagnosticMessage($"{rows} rows imported in {stopwatch.ElapsedMilliseconds} ms");
        Assert.Equal(rows, batch.NewCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(maxSeconds), $"{rows} rows took {stopwatch.Elapsed}.");
    }

    /// <summary>Newest first, a few payments a day, with a consistent running balance.</summary>
    private static string Export(int count, string idPrefix)
    {
        var end = new DateOnly(2026, 10, 28);
        var balance = 100_000m;
        var lines = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var date = end.AddDays(-i / 3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var amount = -(1 + (i % 97)) - 0.25m;
            lines.Add(FakeIngExport.CardRow(
                date,
                amount.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ','),
                $"'{idPrefix}{i:D12}'",
                balance: balance.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')));
            balance -= amount;
        }

        var start = end.AddDays(-count / 3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return FakeIngExport.File(start, "2026-10-28", [.. lines]);
    }
}
