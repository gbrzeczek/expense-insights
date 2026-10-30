using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Classification.Eval;

/// <summary>One statement row: what the pipeline said and what the label says.</summary>
public sealed record EvaluatedRow(int Row, string MerchantKey, decimal Amount, Guid? Expected, RuleMatch? Predicted)
{
    public bool IsClassified => Predicted is not null;

    public bool IsCorrect => Predicted is not null && Predicted.CategoryId == Expected;

    public bool NeedsReview => Predicted is not null && Confidence.NeedsReview(Predicted.Source, Predicted.Confidence);

    /// <summary>The tier that classified it (<c>globalRule/merchantContains</c>), or <c>unclassified</c>.</summary>
    public string Tier => Predicted is null ? "unclassified" : $"{Predicted.Source.ToString().ToLowerInvariant()}/{Predicted.Reason}";
}

/// <summary>Runs the classification pipeline on a statement and compares it with labels (plan 04).</summary>
public static class Evaluation
{
    public static async Task<EvaluationReport> RunAsync(
        IStatementParser parser,
        Stream statement,
        IReadOnlyList<Label> labels,
        IReadOnlyCollection<MerchantRule> userRules,
        string? accountHolder,
        CancellationToken ct)
    {
        var ownAccounts = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<ImportedTransaction>();
        await foreach (var result in parser.ParseAsync(statement, ct))
        {
            switch (result)
            {
                case ParsedStatement s:
                    ownAccounts.UnionWith(s.Statement.Accounts.Select(a => AccountNumber.Hash(a.Number)).OfType<string>());
                    break;
                case ParsedTransaction t:
                    rows.Add(t.Transaction);
                    break;
            }
        }

        // Labels match by row when the files line up, otherwise by the bank's id.
        var byRow = labels.Where(l => l.Row is not null).ToDictionary(l => l.Row!.Value);
        var byId = labels.Where(l => l.ExternalId is not null).GroupBy(l => l.ExternalId!).ToDictionary(g => g.Key, g => g.First());
        var lineUp = labels.Count == rows.Count && byRow.Count == rows.Count;
        var holder = RuleEngine.NameWords(accountHolder);

        var evaluated = new List<EvaluatedRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var label = lineUp ? byRow.GetValueOrDefault(i + 1) : row.ExternalId is { } id ? byId.GetValueOrDefault(id) : null;
            if (label is null)
            {
                continue;
            }

            // As the importer does: a counterparty that is one of the statement's accounts is an own transfer.
            var hash = AccountNumber.Hash(row.CounterpartyAccount);
            var kind = hash is not null && ownAccounts.Contains(hash) ? TransactionKind.OwnTransfer : row.Kind;
            var key = MerchantNormalizer.Normalize(row.CounterpartyName, row.MerchantHint);
            var input = new RuleInput(kind, row.Amount, key, row.TitleRaw, hash);
            var matches = RuleEngine.Evaluate(input, userRules, GlobalRules.Default, holder);
            var best = matches.Count > 0 ? matches[0] : null;
            evaluated.Add(new EvaluatedRow(i + 1, key, row.Amount, label.CategoryId, best));
        }

        return new EvaluationReport(evaluated);
    }
}

public sealed class EvaluationReport(IReadOnlyList<EvaluatedRow> rows)
{
    public IReadOnlyList<EvaluatedRow> Rows { get; } = rows;

    /// <summary>The share of rows the pipeline gave a category (not left unclassified).</summary>
    public double Coverage => Ratio(Rows.Count(r => r.IsClassified), Rows.Count);

    /// <summary>Of the classified rows, the share with the right category.</summary>
    public double Precision => Ratio(Rows.Count(r => r.IsCorrect), Rows.Count(r => r.IsClassified));

    /// <summary>Of the rows classified without needing review, the share with the right category.</summary>
    public double ConfidentPrecision => Ratio(Rows.Count(r => r.IsCorrect && !r.NeedsReview), Rows.Count(r => r.IsClassified && !r.NeedsReview));

    public double Accuracy => Ratio(Rows.Count(r => r.IsCorrect), Rows.Count);

    public int ReviewCount => Rows.Count(r => r.NeedsReview);

    /// <summary>Per tier: rows, correct ones.</summary>
    public IReadOnlyList<(string Tier, int Rows, int Correct)> Tiers =>
        [.. Rows.GroupBy(r => r.Tier).Select(g => (g.Key, g.Count(), g.Count(r => r.IsCorrect))).OrderByDescending(t => t.Item2)];

    /// <summary>(expected, predicted) → count, with <c>null</c> for unclassified.</summary>
    public IReadOnlyDictionary<(Guid? Expected, Guid? Predicted), int> Confusion =>
        Rows.GroupBy(r => (r.Expected, r.Predicted?.CategoryId)).ToDictionary(g => g.Key, g => g.Count());

    private static double Ratio(int part, int whole) => whole == 0 ? 0 : (double)part / whole;
}
