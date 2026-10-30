namespace ExpenseInsights.Core.Classification;

public enum RuleMatchType
{
    /// <summary>The merchant key equals the pattern.</summary>
    Exact,

    /// <summary>A word of the merchant key starts with the pattern (<c>apteka</c> in <c>apteka doz</c>).</summary>
    Contains,

    /// <summary>The merchant key is similar to the pattern (<c>pg_trgm</c> similarity).</summary>
    Trigram,

    /// <summary>The counterparty account's hash equals the pattern: a person or payee by account number.</summary>
    CounterpartyAccount,

    /// <summary>A word of the title starts with the pattern (<c>czynsz</c>, <c>wynagrodzenie</c>).</summary>
    TitleKeyword,
}
