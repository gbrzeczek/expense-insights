namespace ExpenseInsights.Core.Import;

/// <summary>Sanity checks on a parsed statement. Mismatches become warnings, never fixes.</summary>
public static class StatementChecks
{
    /// <summary>At most this many issues per check, so one broken file can't flood the batch.</summary>
    public const int MaxIssuesPerCheck = 20;

    /// <summary>How far (in rows of one account) a row may sit from its place in the balance chain.</summary>
    public const int ReorderWindow = 20;

    /// <summary>
    /// Per account, with rows newest first: <c>Balance[i] = Balance[i + 1] + Amount[i]</c>.
    /// </summary>
    /// <remarks>
    /// Exports don't always list rows in the order the bank applied them: a row may sit a few places
    /// from where its balance belongs (same-day rows, a card payment booked later). So the check
    /// follows the chain: walking oldest first, the next row is the nearest one (within
    /// <see cref="ReorderWindow"/>) whose balance before it equals the balance so far. Only when no
    /// row continues the chain is the next row in file order a mismatch.
    /// </remarks>
    public static (CheckResult Result, IReadOnlyList<ImportIssue> Issues) CheckBalances(
        IReadOnlyList<ImportedTransaction> newestFirst)
    {
        var issues = new List<ImportIssue>();
        var compared = 0;
        var mismatches = 0;

        foreach (var account in newestFirst.Where(t => t.BalanceAfter is not null).GroupBy(t => t.AccountRef))
        {
            var rows = account.Reverse().ToList();
            var placed = new bool[rows.Count];
            var balance = rows[0].BalanceAfter!.Value;
            placed[0] = true;
            var next = 1;
            for (var step = 1; step < rows.Count; step++)
            {
                while (placed[next])
                {
                    next++;
                }

                compared++;
                var follows = Enumerable.Range(next, Math.Min(ReorderWindow, rows.Count - next))
                    .FirstOrDefault(j => !placed[j] && rows[j].BalanceAfter!.Value - rows[j].Amount == balance, -1);
                if (follows < 0)
                {
                    follows = next;
                    mismatches++;
                    issues.Add(new ImportIssue(
                        ImportIssueCodes.BalanceMismatch,
                        LineNumber: rows[next].RawRow.LineNumber,
                        Account: account.Key,
                        Expected: balance + rows[next].Amount,
                        Actual: rows[next].BalanceAfter));
                }

                placed[follows] = true;
                balance = rows[follows].BalanceAfter!.Value;
            }
        }

        var result = compared == 0 ? CheckResult.NotAvailable
            : mismatches == 0 ? CheckResult.Passed
            : CheckResult.Failed;
        return (result, [.. issues.OrderBy(i => i.LineNumber).Take(MaxIssuesPerCheck)]);
    }

    /// <summary>Compares the rows with the statement's declared credit and debit totals.</summary>
    public static (CheckResult Result, IReadOnlyList<ImportIssue> Issues) CheckTotals(
        StatementInfo statement,
        IReadOnlyList<ImportedTransaction> transactions)
    {
        if (statement.Credits is null && statement.Debits is null)
        {
            return (CheckResult.NotAvailable, []);
        }

        var issues = new List<ImportIssue>();
        Compare("credits", statement.Credits, transactions.Where(t => t.Amount > 0).Select(t => t.Amount));
        Compare("debits", statement.Debits, transactions.Where(t => t.Amount < 0).Select(t => -t.Amount));
        return (issues.Count == 0 ? CheckResult.Passed : CheckResult.Failed, issues);

        void Compare(string side, DeclaredTotal? declared, IEnumerable<decimal> amounts)
        {
            if (declared is null)
            {
                return;
            }

            var list = amounts.ToList();
            if (list.Count != declared.Count)
            {
                issues.Add(new ImportIssue(
                    ImportIssueCodes.TotalsCountMismatch, Value: side, Expected: declared.Count, Actual: list.Count));
            }

            if (list.Sum() != declared.Sum)
            {
                issues.Add(new ImportIssue(
                    ImportIssueCodes.TotalsSumMismatch, Value: side, Expected: declared.Sum, Actual: list.Sum()));
            }
        }
    }

    /// <summary>
    /// The days between what earlier imports covered and where this one starts, if any.
    /// </summary>
    public static ImportIssue? FindGap(
        DateOnly? periodStart,
        IEnumerable<(DateOnly Start, DateOnly End)> earlierPeriods)
    {
        if (periodStart is not { } start)
        {
            return null;
        }

        var covered = earlierPeriods.Where(p => p.Start <= start).Select(p => (DateOnly?)p.End).Max();
        if (covered is not { } coveredUntil || coveredUntil.AddDays(1) >= start)
        {
            return null;
        }

        return new ImportIssue(ImportIssueCodes.PeriodGap, From: coveredUntil.AddDays(1), To: start.AddDays(-1));
    }
}
