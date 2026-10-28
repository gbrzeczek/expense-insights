using System.Security.Cryptography;
using System.Text;
using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;
using ExpenseInsights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseInsights.Infrastructure.Import;

/// <param name="Batch">The new batch, or the earlier one when the file was imported before.</param>
/// <param name="AlreadyImported">The same file (by SHA-256) was imported before; nothing changed.</param>
public sealed record ImportOutcome(ImportBatch Batch, bool AlreadyImported);

/// <summary>
/// Imports a statement file for the current user: picks a parser, upserts the accounts, then
/// dedups, reconciles pending holds with booked rows and links own transfers. Bank-agnostic: it
/// only sees <see cref="ParseResult"/>s.
/// </summary>
public sealed partial class StatementImporter(
    AppDbContext db,
    IEnumerable<IStatementParser> parsers,
    TimeProvider clock,
    ILogger<StatementImporter> logger)
{
    public const int HeadSize = 8 * 1024;

    /// <summary>A hold this much older than the end of a newer export that doesn't contain it is expired.</summary>
    public const int ExpireAfterDays = 14;

    /// <param name="file">A seekable stream (it is read twice: once for the hash, once to parse).</param>
    /// <param name="fileName">The uploaded file's name, for display.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<ImportOutcome> ImportAsync(Stream file, string fileName, CancellationToken ct)
    {
        if (!file.CanSeek)
        {
            throw new ArgumentException("The stream must be seekable.", nameof(file));
        }

        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
        var earlier = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.FileHash == hash, ct);
        if (earlier is not null)
        {
            return new ImportOutcome(earlier, AlreadyImported: true);
        }

        var parser = await DetectAsync(file, ct);
        var parsed = await ParseAsync(parser, file, ct);
        var now = clock.GetUtcNow();
        var statement = parsed.Statement;

        var batch = new ImportBatch
        {
            Id = Guid.CreateVersion7(),
            BankId = parser.BankId,
            FileName = DisplayName(fileName),
            FileHash = hash,
            FileSize = file.Length,
            DocumentId = statement.DocumentId,
            GeneratedAt = statement.GeneratedAt,
            PeriodStart = statement.PeriodStart,
            PeriodEnd = statement.PeriodEnd,
            ImportedAt = now,
            RowCount = parsed.Rows.Count + parsed.Errors.Count,
        };
        batch.Warnings.AddRange(parsed.Warnings);

        var (totals, totalsIssues) = StatementChecks.CheckTotals(statement, parsed.Rows);
        var (balances, balanceIssues) = StatementChecks.CheckBalances(parsed.Rows);
        batch.TotalsCheck = totals;
        batch.BalanceCheck = balances;
        batch.Warnings.AddRange(totalsIssues);
        batch.Warnings.AddRange(balanceIssues);
        if (await FindGapAsync(parser.BankId, statement.PeriodStart, ct) is { } gap)
        {
            batch.Warnings.Add(gap);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.ImportBatches.Add(batch);

        var accounts = await UpsertAccountsAsync(parser.BankId, statement, now, ct);
        var rows = new List<(ImportedTransaction Row, Account Account)>(parsed.Rows.Count);
        var errors = new List<ImportIssue>(parsed.Errors);
        foreach (var row in parsed.Rows)
        {
            if (accounts.TryGetValue(row.AccountRef, out var account))
            {
                rows.Add((row, account));
            }
            else
            {
                errors.Add(new ImportIssue(ImportIssueCodes.UnknownAccount, LineNumber: row.RawRow.LineNumber, Account: row.AccountRef));
            }
        }

        batch.Errors.AddRange(errors.OrderBy(e => e.LineNumber));
        batch.ErrorCount = errors.Count;

        var touched = await ApplyRowsAsync(batch, rows, [.. accounts.Values.Select(a => a.Id)], now, ct);
        var ownAccounts = db.Accounts.Local.ToDictionary(a => a.NumberHash, a => a.Id);
        MarkOwnTransfers(touched, ownAccounts);
        await db.SaveChangesAsync(ct);

        // The legs point at each other, so they are linked once both rows exist.
        await PairOwnTransfersAsync(touched, ownAccounts, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        foreach (var warning in parsed.Warnings.Where(w => w.Code == ImportIssueCodes.UnknownDetailsCode))
        {
            LogUnknownCode(batch.Id, warning.Value);
        }

        LogImported(batch.Id, batch.NewCount, batch.UpdatedCount, batch.DuplicateCount, batch.ErrorCount, batch.ExpiredCount);
        return new ImportOutcome(batch, AlreadyImported: false);
    }

    private async Task<IStatementParser> DetectAsync(Stream file, CancellationToken ct)
    {
        file.Position = 0;
        var head = new byte[HeadSize];
        var read = await file.ReadAtLeastAsync(head, HeadSize, throwOnEndOfStream: false, ct);
        file.Position = 0;

        var matches = parsers
            .Select(p => (Parser: p, Match: p.Detect(head.AsSpan(0, read))))
            .Where(m => m.Match != ParserMatch.None)
            .OrderByDescending(m => m.Match)
            .ToList();
        return matches.Count > 0
            ? matches[0].Parser
            : throw new StatementFormatException(ImportIssueCodes.UnsupportedFormat, "No parser recognises the file.");
    }

    private static async Task<ParsedFile> ParseAsync(IStatementParser parser, Stream file, CancellationToken ct)
    {
        StatementInfo? statement = null;
        var parsed = new ParsedFile();
        await foreach (var result in parser.ParseAsync(file, ct))
        {
            switch (result)
            {
                case ParsedStatement s:
                    statement = s.Statement;
                    break;
                case ParsedTransaction t:
                    parsed.Rows.Add(t.Transaction);
                    break;
                case ParseError e:
                    parsed.Errors.Add(e.Issue);
                    break;
                case ParseWarning w:
                    parsed.Warnings.Add(w.Issue);
                    break;
            }
        }

        parsed.Statement = statement ?? throw new InvalidOperationException($"Parser {parser.BankId} yielded no statement.");
        return parsed;
    }

    private async Task<ImportIssue?> FindGapAsync(string bankId, DateOnly? periodStart, CancellationToken ct)
    {
        var periods = await db.ImportBatches.AsNoTracking()
            .Where(b => b.BankId == bankId && b.PeriodStart != null && b.PeriodEnd != null)
            .Select(b => new { Start = b.PeriodStart!.Value, End = b.PeriodEnd!.Value })
            .ToListAsync(ct);
        return StatementChecks.FindGap(periodStart, periods.Select(p => (p.Start, p.End)));
    }

    /// <summary>One account per statement entry, found by number hash; the name and currency follow the bank.</summary>
    private async Task<Dictionary<string, Account>> UpsertAccountsAsync(
        string bankId, StatementInfo statement, DateTimeOffset now, CancellationToken ct)
    {
        // All of the user's accounts get tracked: own transfers can go to any of them.
        var existing = (await db.Accounts.ToListAsync(ct)).Where(a => a.BankId == bankId).ToList();
        var byRef = new Dictionary<string, Account>(StringComparer.Ordinal);
        foreach (var listed in statement.Accounts)
        {
            var hash = AccountNumber.Hash(listed.Number) ?? HashName(listed.Ref);
            var account = existing.FirstOrDefault(a => a.NumberHash == hash);
            if (account is null)
            {
                account = new Account
                {
                    Id = Guid.CreateVersion7(),
                    BankId = bankId,
                    Name = listed.Ref,
                    NumberMasked = AccountNumber.Mask(listed.Number),
                    NumberHash = hash,
                    Currency = listed.Currency,
                    CreatedAt = now,
                };
                db.Accounts.Add(account);
                existing.Add(account);
            }
            else
            {
                account.Name = listed.Ref;
                account.Currency = listed.Currency;
            }

            byRef[listed.Ref] = account;
        }

        return byRef;
    }

    /// <summary>Dedups and inserts rows, promotes holds that got booked, and expires stale ones.</summary>
    private async Task<List<Transaction>> ApplyRowsAsync(
        ImportBatch batch,
        List<(ImportedTransaction Row, Account Account)> rows,
        List<Guid> accountIds,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var externalIds = rows.Select(r => r.Row.ExternalId).OfType<string>().Distinct().ToList();
        var known = (await db.Transactions.AsNoTracking()
                .Where(t => accountIds.Contains(t.AccountId) && t.ExternalId != null && externalIds.Contains(t.ExternalId))
                .Select(t => new { t.AccountId, t.ExternalId })
                .ToListAsync(ct))
            .Select(t => (t.AccountId, t.ExternalId!))
            .ToHashSet();

        var storedHolds = await db.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Status == TransactionStatus.Pending)
            .ToListAsync(ct);
        var incomingHolds = rows.Where(r => r.Row.Status == TransactionStatus.Pending).ToList();
        var bookedHolds = await LoadBookedNearAsync(accountIds, incomingHolds.Select(r => r.Row), ct);

        var claimedHolds = new HashSet<Guid>();
        var claimedBooked = new HashSet<Guid>();
        var touched = new List<Transaction>();
        var booked = new List<(ImportedTransaction Row, Account Account)>();

        foreach (var (row, account) in rows)
        {
            if (row.Status == TransactionStatus.Booked)
            {
                if (known.Add((account.Id, row.ExternalId!)))
                {
                    booked.Add((row, account));
                }
                else
                {
                    batch.DuplicateCount++;
                }

                continue;
            }

            // A hold seen in an earlier export, still pending or already booked.
            var stillPending = storedHolds.FirstOrDefault(h =>
                h.AccountId == account.Id && !claimedHolds.Contains(h.Id) && PendingReconciliation.IsSameHold(row, h));
            if (stillPending is not null)
            {
                claimedHolds.Add(stillPending.Id);
                stillPending.BalanceAfter = row.BalanceAfter;
                batch.DuplicateCount++;
                continue;
            }

            var alreadyBooked = bookedHolds.FirstOrDefault(b =>
                b.AccountId == account.Id && !claimedBooked.Contains(b.Id) && PendingReconciliation.IsSameHold(row, b));
            if (alreadyBooked is not null)
            {
                claimedBooked.Add(alreadyBooked.Id);
                batch.DuplicateCount++;
                continue;
            }

            touched.Add(Insert(batch, row, account, now));
        }

        foreach (var (row, account) in booked)
        {
            var hold = PendingReconciliation.FindPending(
                row, storedHolds.Where(h => h.AccountId == account.Id && !claimedHolds.Contains(h.Id)));
            if (hold is null)
            {
                touched.Add(Insert(batch, row, account, now));
                continue;
            }

            // Promote in place: the id, category and user edits stay.
            claimedHolds.Add(hold.Id);
            hold.ApplyImported(row);
            hold.UpdatedAt = now;
            batch.UpdatedCount++;
            touched.Add(hold);
        }

        if (batch.PeriodStart is { } start && batch.PeriodEnd is { } end)
        {
            foreach (var hold in storedHolds.Where(h => !claimedHolds.Contains(h.Id)
                && h.MatchDate >= start
                && h.MatchDate <= end.AddDays(-ExpireAfterDays)))
            {
                hold.Status = TransactionStatus.Expired;
                hold.UpdatedAt = now;
                batch.ExpiredCount++;
            }
        }

        return touched;
    }

    private async Task<List<Transaction>> LoadBookedNearAsync(
        List<Guid> accountIds, IEnumerable<ImportedTransaction> holds, CancellationToken ct)
    {
        var dates = holds.Select(h => h.PurchaseDate ?? h.TransactionDate).ToList();
        if (dates.Count == 0)
        {
            return [];
        }

        var (from, to) = (dates.Min(), dates.Max());
        return await db.Transactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId)
                && t.Status == TransactionStatus.Booked
                && (t.PurchaseDate ?? t.TransactionDate) >= from
                && (t.PurchaseDate ?? t.TransactionDate) <= to)
            .ToListAsync(ct);
    }

    private Transaction Insert(ImportBatch batch, ImportedTransaction row, Account account, DateTimeOffset now)
    {
        var transaction = new Transaction
        {
            Id = Guid.CreateVersion7(),
            AccountId = account.Id,
            ImportBatchId = batch.Id,
            Currency = row.Currency,
            CounterpartyRaw = row.CounterpartyRaw,
            TitleRaw = row.TitleRaw,
            CounterpartyName = row.CounterpartyName,
            RawRow = row.RawRow,
            CreatedAt = now,
            UpdatedAt = now,
        };
        transaction.ApplyImported(row);
        db.Transactions.Add(transaction);
        batch.NewCount++;
        return transaction;
    }

    /// <summary>Rows whose counterparty is one of the user's own accounts are own transfers.</summary>
    private static void MarkOwnTransfers(List<Transaction> touched, Dictionary<string, Guid> ownAccounts)
    {
        foreach (var transaction in touched)
        {
            if (AccountNumber.Hash(transaction.CounterpartyAccount) is { } hash && ownAccounts.ContainsKey(hash))
            {
                transaction.Kind = TransactionKind.OwnTransfer;
            }
        }
    }

    /// <summary>
    /// Links a debit on A to B with the matching credit on B, including a leg imported earlier.
    /// </summary>
    private async Task PairOwnTransfersAsync(
        List<Transaction> touched, Dictionary<string, Guid> ownAccounts, DateTimeOffset now, CancellationToken ct)
    {
        var legs = touched.Where(t => t.Kind == TransactionKind.OwnTransfer && t.OwnTransferPairId is null).ToList();
        if (legs.Count == 0)
        {
            return;
        }

        var (from, to) = (legs.Min(l => l.TransactionDate).AddDays(-OwnTransferPairing.MaxDaysApart),
            legs.Max(l => l.TransactionDate).AddDays(OwnTransferPairing.MaxDaysApart));
        var ids = legs.Select(l => l.Id).ToHashSet();
        legs.AddRange(await db.Transactions
            .Where(t => t.Kind == TransactionKind.OwnTransfer
                && t.OwnTransferPairId == null
                && t.TransactionDate >= from
                && t.TransactionDate <= to
                && !ids.Contains(t.Id))
            .ToListAsync(ct));

        var byId = legs.ToDictionary(l => l.Id);
        var candidates = legs
            .Select(l => new OwnTransferLeg(
                l.Id,
                l.AccountId,
                AccountNumber.Hash(l.CounterpartyAccount) is { } hash && ownAccounts.TryGetValue(hash, out var other) ? other : null,
                l.Amount,
                l.TransactionDate))
            .ToList();
        foreach (var (debitId, creditId) in OwnTransferPairing.Pair(candidates))
        {
            byId[debitId].OwnTransferPairId = creditId;
            byId[creditId].OwnTransferPairId = debitId;
            byId[debitId].UpdatedAt = now;
            byId[creditId].UpdatedAt = now;
        }
    }

    private static string HashName(string name) =>
        "name:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..59];

    private static string DisplayName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        return name.Length <= 255 ? name : name[..255];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Import {BatchId}: {New} new, {Updated} updated, {Duplicates} duplicates, {Errors} errors, {Expired} expired")]
    private partial void LogImported(Guid batchId, int @new, int updated, int duplicates, int errors, int expired);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import {BatchId}: unknown transaction details code {Code}")]
    private partial void LogUnknownCode(Guid batchId, string? code);

    private sealed class ParsedFile
    {
        public StatementInfo Statement { get; set; } = null!;

        public List<ImportedTransaction> Rows { get; } = [];

        public List<ImportIssue> Errors { get; } = [];

        public List<ImportIssue> Warnings { get; } = [];
    }
}
