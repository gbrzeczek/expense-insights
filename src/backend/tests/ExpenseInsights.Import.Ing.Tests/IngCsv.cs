using System.Text;

namespace ExpenseInsights.Import.Ing.Tests;

/// <summary>Builds small ING exports in the real layout (fake values) for focused tests.</summary>
internal static class IngCsv
{
    public const string MainAccount = "KONTO Mobi pow.26";
    public const string SavingsAccount = "Otwarte Konto Oszczędnościowe";
    public const string MainNumber = "12 1043 3218 1960 0133 8908 3863";
    public const string SavingsNumber = "74 7940 2654 2351 1615 5940 7816";
    public const string PreamblePii = "KOWALSKI JAN PAWEŁ , UL. FAŁSZYWA 1 00-001 WARSZAWA";

    public static readonly Encoding Windows1250 = CodePagesEncodingProvider.Instance.GetEncoding(1250)!;

    public const string HeaderLine =
        "\"Data transakcji\";\"Data księgowania\";\"Dane kontrahenta\";\"Tytuł\";\"Nr rachunku\";\"Nazwa banku\";\"Szczegóły\";\"Nr transakcji\";\"Kwota transakcji (waluta rachunku)\";\"Waluta\";\"Kwota blokady/zwolnienie blokady\";\"Waluta\";\"Kwota płatności w walucie\";\"Waluta\";\"Konto\";\"Saldo po transakcji\";\"Waluta\";;;;";

    public static IReadOnlyList<string> Preamble(int credits = 0, string creditSum = "0,00", int debits = 0, string debitSum = "0,00") =>
    [
        "\"Lista transakcji\";;;;;\"ING Bank Śląski S.A. ul. Sokolska 34, 40-086 Katowice www.ing.pl\";;;;;;;;;;;;;;;",
        "\"Dokument nr 0123456789_281026\";",
        "\"Wygenerowany dnia: 2026-10-28, 12:36\";;;;;;;;;;;;;;;;;;;;",
        "",
        "\"Dane Użytkownika:\";",
        $"\"{PreamblePii}\";",
        "",
        "\"Wybrane rachunki:\";",
        $"\"{MainAccount} (PLN)\";;\"{MainNumber}\";",
        $"\"{SavingsAccount} (PLN)\";;\"{SavingsNumber}\";",
        "",
        "\"Zastosowane kryteria wyboru\";;;;;\"Podsumowanie\";;",
        "",
        "\"Zakres dat:\";\"2026-05-01 - 2026-10-28\";\"Typy transakcji:\";\"wszystkie\";;;;",
        "",
        $";;;;;\"Suma uznań ({credits}):\";{creditSum};PLN;;;;;;;;;;;;;",
        "",
        $";;;;;\"Suma obciążeń ({debits}):\";{debitSum};PLN;;;;;;;;;;;;;",
        "",
    ];

    public static readonly string[] Footer = ["", "", "\"Dokument ma charakter informacyjny, nie stanowi dowodu księgowego\";"];

    /// <summary>A whole file: preamble, header, the given data lines and the footer.</summary>
    public static string File(params string[] rows) => string.Join('\n', [.. Preamble(), HeaderLine, .. rows, .. Footer]) + "\n";

    public static MemoryStream Stream(string text, Encoding? encoding = null) => new((encoding ?? Windows1250).GetBytes(text));

    /// <summary>A booked card payment line on the main account.</summary>
    public static string CardRow(
        string date = "2026-10-26",
        string booked = "2026-10-26",
        string counterparty = " LIDL KAMIENSKIEGO  KRAKOW  POL ",
        string amount = "-202,75",
        string id = "'202629999127231238'",
        string fxAmount = "",
        string fxCurrency = "",
        string balance = "38787,53") =>
        $"{date};{booked};\"{counterparty}\";\" Płatność kartą {date[8..10]}.{date[5..7]}.{date[..4]} Nr karty 4111xx1234\";'1915031/19730 ';;\"TR.KART  \";{id};{amount};PLN;;;{fxAmount};{fxCurrency};\"{MainAccount}\";{balance};PLN;;;;";

    public static string TestDataPath(string name) => Path.Combine(AppContext.BaseDirectory, "test-data", name);
}
