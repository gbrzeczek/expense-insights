using ExpenseInsights.Core.Import;
using static ExpenseInsights.Core.Tests.Import.Fakes;

namespace ExpenseInsights.Core.Tests.Import;

public sealed class StatementChecksTests
{
    [Fact]
    public void Balances_Pass_WhenEachRowFollowsTheOlderOne_PerAccount()
    {
        // Newest first; the two accounts interleave.
        ImportedTransaction[] rows =
        [
            Imported(-10m, 90m, line: 1),
            Imported(500m, 1500m, account: "Savings", line: 2),
            Imported(-20m, 100m, line: 3),
            Imported(0m, 1000m, account: "Savings", line: 4),
            Imported(-5m, 120m, line: 5),
        ];

        var (result, issues) = StatementChecks.CheckBalances(rows);

        Assert.Equal(CheckResult.Passed, result);
        Assert.Empty(issues);
    }

    [Fact]
    public void Balances_Fail_WithTheMismatchedLine()
    {
        ImportedTransaction[] rows = [Imported(-10m, 95m, line: 7), Imported(-20m, 100m, line: 8)];

        var (result, issues) = StatementChecks.CheckBalances(rows);

        Assert.Equal(CheckResult.Failed, result);
        var issue = Assert.Single(issues);
        Assert.Equal(ImportIssueCodes.BalanceMismatch, issue.Code);
        Assert.Equal(7, issue.LineNumber);
        Assert.Equal("Main", issue.Account);
        Assert.Equal(90m, issue.Expected);
        Assert.Equal(95m, issue.Actual);
    }

    [Fact]
    public void Balances_AreNotAvailable_WithoutBalances()
    {
        Assert.Equal(CheckResult.NotAvailable, StatementChecks.CheckBalances([Imported(-1m), Imported(-2m)]).Result);
    }

    [Fact]
    public void Balances_CapTheNumberOfIssues()
    {
        var rows = Enumerable.Range(0, 100).Select(i => Imported(-1m, 0m, line: i)).ToList();

        var (_, issues) = StatementChecks.CheckBalances(rows);

        Assert.Equal(StatementChecks.MaxIssuesPerCheck, issues.Count);
    }

    [Fact]
    public void Totals_Pass_WhenCountsAndSumsMatch()
    {
        var statement = Statement(new DeclaredTotal(1, 100m, "PLN"), new DeclaredTotal(2, 30.50m, "PLN"));

        var (result, issues) = StatementChecks.CheckTotals(statement, [Imported(100m), Imported(-10m), Imported(-20.50m)]);

        Assert.Equal(CheckResult.Passed, result);
        Assert.Empty(issues);
    }

    [Fact]
    public void Totals_Fail_OnCountOrSumMismatch()
    {
        var statement = Statement(new DeclaredTotal(2, 100m, "PLN"), new DeclaredTotal(1, 10m, "PLN"));

        var (result, issues) = StatementChecks.CheckTotals(statement, [Imported(100m), Imported(-10.01m)]);

        Assert.Equal(CheckResult.Failed, result);
        Assert.Equal(
            [
                new ImportIssue(ImportIssueCodes.TotalsCountMismatch, Value: "credits", Expected: 2, Actual: 1),
                new ImportIssue(ImportIssueCodes.TotalsSumMismatch, Value: "debits", Expected: 10m, Actual: 10.01m),
            ],
            issues);
    }

    [Fact]
    public void Totals_AreNotAvailable_WithoutDeclaredTotals()
    {
        Assert.Equal(CheckResult.NotAvailable, StatementChecks.CheckTotals(Statement(null, null), [Imported(1m)]).Result);
    }

    [Fact]
    public void Gap_IsReported_BetweenCoveredPeriodAndNewStart()
    {
        var issue = StatementChecks.FindGap(new DateOnly(2026, 9, 11), [(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 2))]);

        Assert.Equal(new ImportIssue(ImportIssueCodes.PeriodGap, From: new DateOnly(2026, 9, 3), To: new DateOnly(2026, 9, 10)), issue);
    }

    [Theory]
    [InlineData("2026-09-03")] // starts the day after
    [InlineData("2026-08-15")] // overlaps
    [InlineData("2026-07-01")] // earlier than everything imported before
    public void Gap_IsNotReported_WhenPeriodsTouchOrOverlap(string start)
    {
        var issue = StatementChecks.FindGap(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), [(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 2))]);

        Assert.Null(issue);
    }

    [Fact]
    public void Gap_IsNotReported_ForTheFirstImport()
    {
        Assert.Null(StatementChecks.FindGap(new DateOnly(2026, 9, 11), []));
    }

    private static StatementInfo Statement(DeclaredTotal? credits, DeclaredTotal? debits) =>
        new() { BankId = "test", Credits = credits, Debits = debits };
}
