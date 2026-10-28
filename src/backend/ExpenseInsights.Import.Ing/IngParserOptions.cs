namespace ExpenseInsights.Import.Ing;

/// <summary>Bound from the <c>Import:Ing</c> configuration section.</summary>
public sealed class IngParserOptions
{
    public const string Section = "Import:Ing";

    /// <summary>
    /// Payment processors (PayU, Cashbill, …) that show up as the counterparty of BLIK and online
    /// payments; for those, the real shop is the domain at the end of the title.
    /// </summary>
    public IReadOnlyList<string> PaymentProcessors { get; init; } = [];
}
