using System.Globalization;
using System.Text.RegularExpressions;
using ExpenseInsights.Core.Import;

namespace ExpenseInsights.Import.Ing;

/// <summary>
/// Collects what we keep from the lines before the header: document number, generation time,
/// accounts, date range and totals. The user's name and address are skipped and never kept.
/// </summary>
internal sealed partial class IngPreamble
{
    private static readonly TimeZoneInfo? Warsaw =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var zone) ? zone : null;

    private readonly List<StatementAccount> _accounts = [];
    private Section _section;
    private string? _documentId;
    private DateTimeOffset? _generatedAt;
    private DateOnly? _periodStart;
    private DateOnly? _periodEnd;
    private DeclaredTotal? _credits;
    private DeclaredTotal? _debits;

    private enum Section
    {
        None,
        User,
        Accounts,
    }

    public void Read(List<string> fields)
    {
        var first = IngFields.Clean(fields[0]);
        if (fields.All(f => f.Trim().Length == 0))
        {
            _section = Section.None;
            return;
        }

        switch (_section)
        {
            case Section.User:
                // "SURNAME NAMES , street, postcode city": personal data, deliberately dropped.
                return;
            case Section.Accounts when AccountLine().Match(first) is { Success: true } account && fields.Count > 2:
                _accounts.Add(new StatementAccount(
                    account.Groups["name"].Value.Trim(),
                    IngFields.Clean(fields[2]),
                    account.Groups["currency"].Value));
                return;
        }

        if (first.StartsWith("Dane Użytkownika", StringComparison.OrdinalIgnoreCase))
        {
            _section = Section.User;
        }
        else if (first.StartsWith("Wybrane rachunki", StringComparison.OrdinalIgnoreCase))
        {
            _section = Section.Accounts;
        }
        else if (first.StartsWith("Dokument nr ", StringComparison.OrdinalIgnoreCase))
        {
            _documentId = first["Dokument nr ".Length..].Trim();
        }
        else if (GeneratedLine().Match(first) is { Success: true } generated)
        {
            _generatedAt = ParseGeneratedAt(generated.Groups["at"].Value);
        }
        else if (first.StartsWith("Zakres dat", StringComparison.OrdinalIgnoreCase) && fields.Count > 1
            && DateRange().Match(IngFields.Clean(fields[1])) is { Success: true } range)
        {
            _periodStart = DateOnly.ParseExact(range.Groups["from"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            _periodEnd = DateOnly.ParseExact(range.Groups["to"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else
        {
            ReadTotals(fields);
        }
    }

    public StatementInfo ToStatement() => new()
    {
        BankId = IngCsvParser.Id,
        DocumentId = _documentId,
        GeneratedAt = _generatedAt,
        PeriodStart = _periodStart,
        PeriodEnd = _periodEnd,
        Accounts = _accounts,
        Credits = _credits,
        Debits = _debits,
    };

    // ;;;;;"Suma uznań (412):";398120,40;PLN;;…
    private void ReadTotals(List<string> fields)
    {
        for (var i = 0; i + 2 < fields.Count; i++)
        {
            var match = TotalsLabel().Match(IngFields.Clean(fields[i]));
            if (!match.Success || !IngFields.TryParseAmount(fields[i + 1], out var sum) || sum is null)
            {
                continue;
            }

            var total = new DeclaredTotal(
                int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture),
                Math.Abs(sum.Value),
                IngFields.Clean(fields[i + 2]));
            if (match.Groups["kind"].Value.StartsWith("uzna", StringComparison.OrdinalIgnoreCase))
            {
                _credits = total;
            }
            else
            {
                _debits = total;
            }

            return;
        }
    }

    private static DateTimeOffset? ParseGeneratedAt(string value)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd, HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        // ING prints Polish local time.
        var offset = Warsaw?.GetUtcOffset(local) ?? TimeSpan.Zero;
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    // "KONTO Mobi pow.26 (PLN)"
    [GeneratedRegex(@"^(?<name>.+?)\s*\((?<currency>[A-Z]{3})\)$")]
    private static partial Regex AccountLine();

    [GeneratedRegex(@"^Wygenerowany dnia:\s*(?<at>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedLine();

    [GeneratedRegex(@"^(?<from>\d{4}-\d{2}-\d{2})\s*-\s*(?<to>\d{4}-\d{2}-\d{2})$")]
    private static partial Regex DateRange();

    [GeneratedRegex(@"^Suma (?<kind>uznań|obciążeń)\s*\((?<count>\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex TotalsLabel();
}
