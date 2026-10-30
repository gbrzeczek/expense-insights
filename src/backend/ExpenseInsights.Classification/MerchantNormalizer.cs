using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ExpenseInsights.Classification;

/// <summary>
/// Turns a counterparty into a merchant key that is the same for every store, terminal and city
/// of a merchant: <c>JMP S.A. BIEDRONKA 6537  KRAKOW  PO</c> → <c>biedronka</c>. Rules match
/// against the key and transactions are grouped by it.
/// </summary>
public static partial class MerchantNormalizer
{
    /// <summary>Tokens that only say where the store is. Matched after diacritics are removed.</summary>
    private static readonly HashSet<string> Places = new(StringComparer.Ordinal)
    {
        // Countries, including the truncated forms card terminals print.
        "p", "po", "pol", "pl", "polska", "poland", "deu", "de", "est", "ee", "swe", "se", "nld", "nl", "irl", "ie",
        "gbr", "gb", "uk", "usa", "us", "lux", "lu", "cze", "cz", "svk", "sk", "ltu", "lt", "esp", "es", "fra", "fr",
        "ita", "it", "aut", "at", "che", "ch", "cyp", "cy", "mlt", "mt", "dnk", "dk", "nor", "no", "fin", "fi",
        // Polish cities: the largest ones and common card-terminal spellings.
        "warszawa", "krakow", "lodz", "wroclaw", "poznan", "gdansk", "szczecin", "bydgoszcz", "lublin", "bialystok",
        "katowice", "gdynia", "czestochowa", "radom", "torun", "sosnowiec", "rzeszow", "kielce", "gliwice", "olsztyn",
        "zabrze", "bielsko", "biala", "bytom", "zielona", "gora", "rybnik", "ruda", "slaska", "opole", "tychy",
        "gorzow", "wielkopolski", "elblag", "plock", "dabrowa", "gornicza", "walbrzych", "wloclawek", "tarnow",
        "chorzow", "koszalin", "kalisz", "legnica", "grudziadz", "jaworzno", "slupsk", "jastrzebie", "zdroj",
        "nowy", "sacz", "jelenia", "siedlce", "myslowice", "konin", "pila", "piotrkow", "trybunalski", "inowroclaw",
        "lubin", "ostrow", "suwalki", "gniezno", "glogow", "zamosc", "przemysl", "pruszkow", "piaseczno",
        "wieliczka", "skawina", "zakopane", "sopot", "wegrzce", "zabki", "marki", "legionowo", "otwock",
        // Foreign cities of common online merchants.
        "berlin", "dublin", "amsterdam", "luxembourg", "london", "stockholm", "tallinn", "arteixo", "paris",
        "prague", "praha", "vilnius", "riga", "madrid", "barcelona", "munich", "munchen", "hamburg", "cork",
        "san", "francisco", "los", "gatos",
    };

    /// <summary>Words that describe the store, not the merchant: <c>ORLEN STACJA NR 5422</c>.</summary>
    private static readonly HashSet<string> StoreWords = new(StringComparer.Ordinal)
    {
        "stacja", "nr", "sklep", "gal", "galeria", "ch", "cent", "centrum", "oddzial", "filia", "punkt", "kiosk",
    };

    /// <summary>The key for a transaction; see <see cref="ForCounterparty"/>.</summary>
    /// <param name="counterparty">The cleaned counterparty name.</param>
    /// <param name="merchantHint">For payments through a processor, the shop's domain: it wins.</param>
    public static string Normalize(string counterparty, string? merchantHint) =>
        merchantHint is { Length: > 0 } hint && ForDomain(hint) is { Length: > 0 } fromHint
            ? fromHint
            : ForCounterparty(counterparty);

    public static string ForCounterparty(string counterparty)
    {
        var text = Fold(counterparty).Replace("'", "", StringComparison.Ordinal).Replace("\u2019", "", StringComparison.Ordinal);

        // "GOOGLE *YouTubePremium": the merchant is after the star.
        var star = text.IndexOf('*', StringComparison.Ordinal);
        if (star >= 0 && star < text.Length - 1)
        {
            text = text[(star + 1)..];
        }

        // The address: "UL. DŁUGA 1 31-147 KRAKÓW", "ul. Polna, nr 3".
        text = Address().Replace(text, "");
        text = CardSuffix().Replace(text, " ");
        text = LegalForm().Replace(text, " ");

        var words = new List<string>();
        foreach (var raw in Separator().Split(text))
        {
            if (raw.Length == 0)
            {
                continue;
            }

            // "BOLT.EU/R/4598", "NETFLIX.COM": the name before the domain ending; later URLs are noise.
            var word = raw;
            if (word.Contains('.', StringComparison.Ordinal) || word.Contains('/', StringComparison.Ordinal))
            {
                if (words.Count > 0)
                {
                    continue;
                }

                word = ForDomain(word.Split('/')[0]);
            }

            foreach (var part in word.Split([' ', '.', '-'], StringSplitOptions.RemoveEmptyEntries))
            {
                // Store numbers and terminal codes: Z5521, 6537, P2B7C1D9E4. A leading brand can have
                // digits (4F, LINK4) but not be only digits.
                var isCode = words.Count > 0 ? part.Any(char.IsAsciiDigit) : part.All(char.IsAsciiDigit);
                if (isCode || part.Length < 2 && words.Count > 0)
                {
                    continue;
                }

                words.Add(part);
            }
        }

        // Places anywhere but the start: "STARBUCKS KRAKOW GAL", "LIDL KAMIENSKIEGO KRAKOW POL".
        var kept = words.Where((w, i) => i == 0 || !(Places.Contains(w) || StoreWords.Contains(w))).ToList();
        while (kept.Count > 1 && Places.Contains(kept[^1]))
        {
            kept.RemoveAt(kept.Count - 1);
        }

        var key = string.Join(' ', kept);
        return key.Length > 0 ? Truncate(key) : Truncate(Fold(counterparty).Trim());
    }

    /// <summary><c>https://www.zielone-sklepy.pl/</c> → <c>zielone sklepy</c>.</summary>
    public static string ForDomain(string domain)
    {
        var host = Fold(domain).Trim();
        host = Scheme().Replace(host, "");
        host = host.Split('/', '?', '#')[0];
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
        // Drop the public suffix: "pl", "com.pl", "co.uk", "eu".
        while (labels.Count > 1 && labels[^1].Length <= 3)
        {
            labels.RemoveAt(labels.Count - 1);
            if (labels.Count > 1 && labels[^1] is "com" or "co" or "org" or "net")
            {
                labels.RemoveAt(labels.Count - 1);
            }
        }

        var name = labels.Count > 0 ? labels[^1] : host;
        return Truncate(name.Replace('-', ' ').Trim());
    }

    /// <summary>Lowercase without diacritics: <c>ŻABKA Łódź</c> → <c>zabka lodz</c>.</summary>
    public static string Fold(string text)
    {
        var decomposed = text.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string Truncate(string key) => key.Length <= 200 ? key : key[..200].TrimEnd();

    [GeneratedRegex(@"(\s|,)ul(\.\s*|\s+)\S.*$|\s\d{2}-\d{3}(\s.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Address();

    [GeneratedRegex(@"\bk\.\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex CardSuffix();

    // Ordered so "sp. z o.o." goes before "s.a.". "jmp" is Biedronka's owner, printed before the brand.
    [GeneratedRegex(
        @"\bjmp\s+s\.?\s?a\b\.?|\bsp(\.|olka)?\s*z\s*o(\.\s*o\b\.?|\.|\b)|\bsp\.?\s*(k|j|p)\b\.?|\bs\.\s?a\b\.?|\bsa\b|\bs\.\s?c\b\.?|\bp\.?\s?h\.?\s?u\b\.?|\bf\.?\s?h\.?\s?u\b\.?|\bse\b|\bgmbh\b|\bltd\b\.?|\binc\b\.?|\bb\.v\b\.?|\bsarl\b|\bs\.?r\.?l\b\.?|\bllc\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex LegalForm();

    [GeneratedRegex(@"[\s,;:""()\[\]#!?+&|]+", RegexOptions.CultureInvariant)]
    private static partial Regex Separator();

    [GeneratedRegex(@"^[a-z]+://", RegexOptions.CultureInvariant)]
    private static partial Regex Scheme();
}
