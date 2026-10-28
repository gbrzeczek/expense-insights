using System.Globalization;

namespace ExpenseInsights.Testing;

/// <summary>
/// Builds small ING exports in the real layout with fake values, for import tests that need a
/// specific period or rows. The declared totals are computed from the rows.
/// </summary>
public static class FakeIngExport
{
    public const string MainAccount = "KONTO Mobi pow.26";
    public const string SavingsAccount = "Otwarte Konto Oszczędnościowe";

    /// <summary>The fake user's name and address line, which must never be stored or logged.</summary>
    public const string PreamblePii = "KOWALSKI JAN PAWEŁ , UL. FAŁSZYWA 1 00-001 WARSZAWA";

    private const string Header =
        "\"Data transakcji\";\"Data księgowania\";\"Dane kontrahenta\";\"Tytuł\";\"Nr rachunku\";\"Nazwa banku\";\"Szczegóły\";\"Nr transakcji\";\"Kwota transakcji (waluta rachunku)\";\"Waluta\";\"Kwota blokady/zwolnienie blokady\";\"Waluta\";\"Kwota płatności w walucie\";\"Waluta\";\"Konto\";\"Saldo po transakcji\";\"Waluta\";;;;";

    public static string File(string periodStart, string periodEnd, params string[] rows)
    {
        var amounts = rows.Select(Amount).ToList();
        var credits = amounts.Where(a => a > 0).ToList();
        var debits = amounts.Where(a => a < 0).Select(a => -a).ToList();
        string[] lines =
        [
            "\"Lista transakcji\";;;;;\"ING Bank Śląski S.A. ul. Sokolska 34, 40-086 Katowice www.ing.pl\";;;;;;;;;;;;;;;",
            $"\"Dokument nr 0123456789_{periodEnd[8..10]}{periodEnd[5..7]}{periodEnd[2..4]}\";",
            $"\"Wygenerowany dnia: {periodEnd}, 12:36\";;;;;;;;;;;;;;;;;;;;",
            "",
            "\"Dane Użytkownika:\";",
            $"\"{PreamblePii}\";",
            "",
            "\"Wybrane rachunki:\";",
            $"\"{MainAccount} (PLN)\";;\"12 1043 3218 1960 0133 8908 3863\";",
            $"\"{SavingsAccount} (PLN)\";;\"74 7940 2654 2351 1615 5940 7816\";",
            "",
            "\"Zastosowane kryteria wyboru\";;;;;\"Podsumowanie\";;",
            "",
            $"\"Zakres dat:\";\"{periodStart} - {periodEnd}\";\"Typy transakcji:\";\"wszystkie\";;;;",
            "",
            $";;;;;\"Suma uznań ({credits.Count}):\";{Format(credits.Sum())};PLN;;;;;;;;;;;;;",
            "",
            $";;;;;\"Suma obciążeń ({debits.Count}):\";{Format(debits.Sum())};PLN;;;;;;;;;;;;;",
            "",
            Header,
            .. rows,
            "",
            "",
            "\"Dokument ma charakter informacyjny, nie stanowi dowodu księgowego\";",
        ];
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>A booked card payment on the main account.</summary>
    public static string CardRow(string date, string amount, string id, string counterparty = " LIDL KAMIENSKIEGO  KRAKOW  POL ", string balance = "") =>
        $"{date};{date};\"{counterparty}\";\" Płatność kartą {date[8..10]}.{date[5..7]}.{date[..4]} Nr karty 4111xx1234\";'1915031/19730 ';;\"TR.KART  \";{id};{amount};PLN;;;;;\"{MainAccount}\";{balance};PLN;;;;";

    private static decimal Amount(string row)
    {
        var fields = row.Split(';');
        var value = fields[8].Length > 0 ? fields[8] : fields[10];
        return decimal.Parse(value.Replace(',', '.'), CultureInfo.InvariantCulture);
    }

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
}
