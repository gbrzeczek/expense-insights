using System.Text.RegularExpressions;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Import.Ing;

/// <summary>Maps ING's <c>Szczegóły</c> code and title patterns to a <see cref="TransactionKind"/>.</summary>
internal static partial class IngTransactionKinds
{
    /// <summary>Codes seen in exports. Anything else becomes <see cref="TransactionKind.Other"/> plus a warning.</summary>
    private static readonly Dictionary<string, TransactionKind> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TR.KART"] = TransactionKind.Card,
        ["TR.BLIK"] = TransactionKind.BlikOnline,
        ["P.BLIK"] = TransactionKind.BlikP2P,
        ["PRZELEW"] = TransactionKind.Transfer,
        // Savings interest and the tax on it (codes guessed; see plan 02's open questions).
        ["ODSETKI"] = TransactionKind.Other,
        ["PODATEK"] = TransactionKind.Other,
    };

    /// <param name="code">The cleaned <c>Szczegóły</c> value; empty for pending rows.</param>
    /// <param name="title">The cleaned title.</param>
    /// <param name="unknownCode">Whether <paramref name="code"/> is one we don't know.</param>
    public static TransactionKind Detect(string code, string title, out bool unknownCode)
    {
        unknownCode = false;

        // The title wins: pending rows have no code, and a phone transfer is coded as PRZELEW.
        if (CardTitle().IsMatch(title))
        {
            return TransactionKind.Card;
        }

        if (BlikTitle().IsMatch(title))
        {
            return TransactionKind.BlikOnline;
        }

        if (PhoneTransferTitle().IsMatch(title))
        {
            return TransactionKind.PhoneTransfer;
        }

        if (code.Length == 0)
        {
            return TransactionKind.Other;
        }

        if (Codes.TryGetValue(code, out var kind))
        {
            return kind;
        }

        unknownCode = true;
        return TransactionKind.Other;
    }

    /// <summary>The purchase date in a card payment title (<c>Płatność kartą 25.10.2026 Nr karty …</c>).</summary>
    public static DateOnly? PurchaseDate(string title)
    {
        var match = CardTitle().Match(title);
        return match.Success && IngFields.TryParseDate(match.Groups["date"].Value, out var date) ? date : null;
    }

    // Pending rows have a double space after "kartą".
    [GeneratedRegex(@"^Płatność kartą\s+(?<date>\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CardTitle();

    [GeneratedRegex(@"^Płatność BLIK\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlikTitle();

    // ING writes this title in English.
    [GeneratedRegex(@"^(Telephone transfer|Przelew na telefon)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhoneTransferTitle();
}
