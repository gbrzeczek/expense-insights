using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ExpenseInsights.Core.Accounts;

/// <summary>Helpers that let account numbers be matched and shown without being stored.</summary>
public static partial class AccountNumber
{
    /// <summary>
    /// Digits only (drops spaces and an IBAN country prefix); <c>null</c> for anything that isn't
    /// an account number, such as a card acquirer id (<c>1915031/19730</c>).
    /// </summary>
    public static string? Normalize(string? number)
    {
        if (number is null || !Shape().IsMatch(number))
        {
            return null;
        }

        var digits = string.Concat(number.Where(char.IsAsciiDigit));
        return digits.Length >= 10 ? digits : null;
    }

    public static string? Hash(string? number) =>
        Normalize(number) is { } digits
            ? Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(digits)))
            : null;

    public static string Mask(string number) =>
        Normalize(number) is { } digits ? $"•••• {digits[^4..]}" : "••••";

    [GeneratedRegex(@"^\s*([A-Za-z]{2})?[\d ]+$")]
    private static partial Regex Shape();
}
