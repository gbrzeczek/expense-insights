using System.Globalization;
using System.Text;
using System.Text.Json;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Import.Ing.Tests;

/// <summary>The whole of <c>test-data/fake_ing.csv</c>, checked against its labels file.</summary>
public sealed class FakeExportTests
{
    private static Task<List<ParseResult>> ParseFakeAsync() =>
        Parse.AllAsync(File.OpenRead(IngCsv.TestDataPath("fake_ing.csv")));

    [Fact]
    public async Task FakeExport_ParsesWithoutErrors()
    {
        var results = await ParseFakeAsync();

        Assert.Empty(results.OfType<ParseError>());
        Assert.Empty(results.OfType<ParseWarning>());
        Assert.Equal(367, results.Transactions().Count());
        Assert.IsType<ParsedStatement>(results[0]);
    }

    [Fact]
    public async Task FakeExport_ReadsPreamble()
    {
        var statement = Assert.IsType<ParsedStatement>((await ParseFakeAsync())[0]).Statement;

        Assert.Equal("ing-pl", statement.BankId);
        Assert.Equal("0123456789_281026", statement.DocumentId);
        // 12:36 Polish time; 28 Oct 2026 is after the switch to CET (UTC+1).
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 11, 36, 0, TimeSpan.Zero), statement.GeneratedAt);
        Assert.Equal(new DateOnly(2026, 5, 1), statement.PeriodStart);
        Assert.Equal(new DateOnly(2026, 10, 28), statement.PeriodEnd);
        Assert.Equal(
            [
                new StatementAccount("KONTO Mobi pow.26", "12 1043 3218 1960 0133 8908 3863", "PLN"),
                new StatementAccount("Otwarte Konto Oszczędnościowe", "74 7940 2654 2351 1615 5940 7816", "PLN"),
            ],
            statement.Accounts);
        Assert.Equal(new DeclaredTotal(33, 74787.45m, "PLN"), statement.Credits);
        Assert.Equal(new DeclaredTotal(334, 43506.39m, "PLN"), statement.Debits);
    }

    [Fact]
    public async Task FakeExport_PassesTotalsAndBalanceChecks()
    {
        var results = await ParseFakeAsync();
        var statement = results.OfType<ParsedStatement>().Single().Statement;
        var transactions = results.Transactions().ToList();

        Assert.Equal(CheckResult.Passed, StatementChecks.CheckTotals(statement, transactions).Result);
        Assert.Equal(CheckResult.Passed, StatementChecks.CheckBalances(transactions).Result);
    }

    [Fact]
    public async Task FakeExport_MatchesLabels()
    {
        var transactions = (await ParseFakeAsync()).Transactions().ToList();
        var labels = File.ReadAllLines(IngCsv.TestDataPath("fake_ing.labels.csv"), Encoding.UTF8)
            .Skip(1)
            .Select(line => line.Split(';'))
            .ToList();

        Assert.Equal(labels.Count, transactions.Count);
        for (var i = 0; i < labels.Count; i++)
        {
            var (label, tx) = (labels[i], transactions[i]);
            Assert.Equal(DateOnly.Parse(label[1], CultureInfo.InvariantCulture), tx.TransactionDate);
            if (label[2].Length > 0 || label[6] == "pending")
            {
                Assert.Equal(label[2].Length == 0 ? null : label[2], tx.ExternalId);
            }
            else
            {
                // Booked without a number: a key from the row's content.
                Assert.StartsWith(IngCsvParser.ContentKeyPrefix, tx.ExternalId, StringComparison.Ordinal);
            }

            Assert.Equal(decimal.Parse(label[3].Replace(',', '.'), CultureInfo.InvariantCulture), tx.Amount);
            Assert.Equal(label[4], tx.AccountRef);
            Assert.Equal(label[6] == "pending" ? TransactionStatus.Pending : TransactionStatus.Booked, tx.Status);
            Assert.Equal(ExpectedKind(label[5]), tx.Kind);
        }
    }

    [Fact]
    public async Task FakeExport_PreamblePiiAppearsInNoResult()
    {
        var results = await ParseFakeAsync();

        var json = JsonSerializer.Serialize(results.Select(r => (object)r).ToList());

        Assert.DoesNotContain("FAŁSZYWA", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("00-001", json, StringComparison.Ordinal);
        Assert.DoesNotContain("KOWALSKI JAN PAWEŁ", json, StringComparison.Ordinal);
    }

    private static TransactionKind ExpectedKind(string generatorKind) => generatorKind switch
    {
        "card" => TransactionKind.Card,
        "blik" => TransactionKind.BlikOnline,
        "p2p_in" => TransactionKind.BlikP2P,
        "p2p_out" => TransactionKind.PhoneTransfer,
        // Own transfers are recognised on import, where the user's accounts are known.
        "transfer_in" or "transfer_out" or "own_transfer" or "standing_order" or "express" or "transfer_order" => TransactionKind.Transfer,
        "interest" or "interest_tax" or "correction" => TransactionKind.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(generatorKind), generatorKind, null),
    };
}
