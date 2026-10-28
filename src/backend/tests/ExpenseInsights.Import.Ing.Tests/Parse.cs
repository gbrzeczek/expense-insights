using ExpenseInsights.Core.Import;

namespace ExpenseInsights.Import.Ing.Tests;

internal static class Parse
{
    public static readonly IngParserOptions Options = new()
    {
        PaymentProcessors = ["PayU", "Cashbill", "PayPro", "Przelewy24"],
    };

    public static async Task<List<ParseResult>> AllAsync(Stream stream)
    {
        var results = new List<ParseResult>();
        await foreach (var result in new IngCsvParser(Options).ParseAsync(stream, TestContext.Current.CancellationToken))
        {
            results.Add(result);
        }

        return results;
    }

    public static Task<List<ParseResult>> AllAsync(string text) => AllAsync(IngCsv.Stream(text));

    public static async Task<ImportedTransaction> SingleAsync(string row)
    {
        var results = await AllAsync(IngCsv.File(row));
        return Assert.Single(results.OfType<ParsedTransaction>()).Transaction;
    }

    public static IEnumerable<ImportedTransaction> Transactions(this IEnumerable<ParseResult> results) =>
        results.OfType<ParsedTransaction>().Select(r => r.Transaction);
}
