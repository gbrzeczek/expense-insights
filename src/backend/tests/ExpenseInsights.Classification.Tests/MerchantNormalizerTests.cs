namespace ExpenseInsights.Classification.Tests;

public sealed class MerchantNormalizerTests
{
    /// <summary>Every counterparty in the fake export (test-data/generate_ing_csv.py), as the parser cleans it.</summary>
    public static TheoryData<string, string> FakeCounterparties => new()
    {
        { "JMP S.A. BIEDRONKA 3803 KRAKOW PO", "biedronka" },
        { "LIDL KAMIENSKIEGO KRAKOW POL", "lidl kamienskiego" },
        { "LIDL DIENSTLEISTUNG Berlin DEU", "lidl dienstleistung" },
        { "ZABKA Z5123 K.1 KRAKOW POL", "zabka" },
        { "KAUFLAND 01 KRAKOW KRAKOW POL", "kaufland" },
        { "MCDONALDS 08573 KRAKOW POL", "mcdonalds" },
        { "STARBUCKS KRAKOW GAL KRAKOW POL", "starbucks" },
        { "ORLEN STACJA NR 5422 KRAKOW POL", "orlen" },
        { "BOLT.EU/R/4598 Tallinn EST", "bolt" },
        { "ZNANYLEKARZ SPOLKA Z O warszawa P", "znanylekarz" },
        { "APTEKA DOZ 1916 KRAKOW POL", "apteka doz" },
        { "CASTORAMA KRAKOW 4 KRAKOW 30964 PO", "castorama" },
        { "IKEA KRAKOW KRAKOW POL", "ikea" },
        { "PEPCO 6175 KRAKOW 14 KRAKOW POL", "pepco" },
        { "ROSSMANN 7685 KRAKOW POL", "rossmann" },
        { "Bershka.com Arteixo", "bershka" },
        { "RESERVED BONARKA KRAKOW POL", "reserved bonarka" },
        { "MEDIAEXPERT WEGRZCE POL", "mediaexpert" },
        { "DECATHLON KRAKOW PL KRAKOW POL", "decathlon" },
        { "Zielony Parapet Krakow POL", "zielony parapet" },
        { "9179 KRAKOW ZAKOPIAN KRAKOW POL", "krakow zakopian" },
        { "P.H.U. ANNA NOWAK KRAKOW POL", "anna nowak" },
        { "NETFLIX.COM Los Gatos NLD", "netflix" },
        { "Spotify P2B7C1D9E4 Stockholm SWE", "spotify" },
        { "GOOGLE *YouTubePremium g.co/helppay# POL", "youtubepremium" },
        { "ZALANDO SE Berlin DEU", "zalando" },
        { "ACME SOFTWARE SP. Z O.O. UL. DŁUGA 1 31-147 KRAKÓW", "acme software" },
        { "SPÓŁDZIELNIA MIESZKANIOWA PODGÓRZE UL. WIELICKA 2 KRAKÓW", "spoldzielnia mieszkaniowa podgorze" },
        { "TAURON SPRZEDAŻ SP. Z O.O.", "tauron sprzedaz" },
        { "ING BANK ŚLĄSKI S.A. UL.SOKOLSKA 34 40-086 KATOWICE", "ing bank slaski" },
        { "JAN PAWEŁ KOWALSKI", "jan pawel kowalski" },
        { "Katarzyna Zielińska", "katarzyna zielinska" },
        { "Michał Wójcik", "michal wojcik" },
        { "Zielone Sklepy Marta Wiśniewska ul. Polna, nr 3, lok. 2 Wieliczka", "zielone sklepy marta wisniewska" },
    };

    [Theory]
    [MemberData(nameof(FakeCounterparties))]
    public void ForCounterparty_StripsStoreDetails(string counterparty, string key) =>
        Assert.Equal(key, MerchantNormalizer.ForCounterparty(counterparty));

    [Theory]
    [InlineData("pyszne.pl", "pyszne")]
    [InlineData("allegro.pl", "allegro")]
    [InlineData("koleo.pl", "koleo")]
    [InlineData("doci.pl", "doci")]
    [InlineData("https://zielone-sklepy.pl", "zielone sklepy")]
    [InlineData("www.sklep.example.com.pl", "example")]
    public void ForDomain_KeepsTheShopName(string domain, string key) =>
        Assert.Equal(key, MerchantNormalizer.ForDomain(domain));

    [Fact]
    public void Normalize_BlikThroughAProcessor_UsesTheShopDomain() =>
        Assert.Equal("pyszne", MerchantNormalizer.Normalize("PayU S.A. Grunwaldzka 186 Poznan", "pyszne.pl"));

    [Fact]
    public void Normalize_WithoutAHint_UsesTheCounterpartyUpToTheAddress() =>
        Assert.Equal("payu grunwaldzka", MerchantNormalizer.Normalize("PayU S.A. Grunwaldzka 186 Poznan", null));

    [Theory]
    [InlineData("McDonald's", "mcdonalds")]
    [InlineData("4F KRAKOW BONARKA KRAKOW POL", "4f bonarka")]
    [InlineData("X-KOM SP. Z O.O.", "x kom")]
    [InlineData("E.LECLERC KRAKOW", "leclerc")]
    [InlineData("CIRCLE K 1234 WARSZAWA PL", "circle")]
    [InlineData("ULUBIONY SKLEP KRAKOW", "ulubiony")]
    [InlineData("12345", "12345")]
    public void ForCounterparty_HandlesBrandQuirks(string counterparty, string key) =>
        Assert.Equal(key, MerchantNormalizer.ForCounterparty(counterparty));

    [Fact]
    public void Fold_RemovesDiacriticsAndCase() => Assert.Equal("zazolc gesla jazn lodz", MerchantNormalizer.Fold("ZAŻÓŁĆ GĘŚLĄ JAŹŃ Łódź"));
}
