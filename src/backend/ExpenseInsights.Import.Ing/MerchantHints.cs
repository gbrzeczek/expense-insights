using System.Text.RegularExpressions;

namespace ExpenseInsights.Import.Ing;

/// <summary>
/// For payments through a processor (<c>PayU S.A. …</c>, <c>Cashbill eComm …</c>), the real shop is
/// only visible as a domain at the end of the title: <c>… Nr transakcji 977… pyszne.pl</c>.
/// </summary>
internal static partial class MerchantHints
{
    public static string? Extract(string counterparty, string title, IReadOnlyList<string> processors)
    {
        if (!processors.Any(p => counterparty.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var lastSpace = title.LastIndexOf(' ');
        var token = lastSpace < 0 ? title : title[(lastSpace + 1)..];
        var match = Domain().Match(token);
        return match.Success ? match.Groups["host"].Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex(
        @"^(?:https?://)?(?:www\.)?(?<host>(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,})(?:[/?#].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Domain();
}
