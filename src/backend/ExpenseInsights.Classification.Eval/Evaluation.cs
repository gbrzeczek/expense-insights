using System.Diagnostics;
using ExpenseInsights.Classification.Embeddings;
using ExpenseInsights.Classification.Tiers;
using ExpenseInsights.Core.Accounts;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Core.Transactions;

namespace ExpenseInsights.Classification.Eval;

/// <summary>One statement row: what the pipeline said and what the label says.</summary>
public sealed record EvaluatedRow(int Row, DateOnly Date, string MerchantKey, decimal Amount, Guid? Expected, RuleMatch? Predicted)
{
    public bool IsClassified => Predicted is not null;

    public bool IsCorrect => Predicted is not null && Predicted.CategoryId == Expected;

    public bool NeedsReview => Predicted is not null && Confidence.NeedsReview(Predicted.Source, Predicted.Confidence);

    /// <summary>Left for the user: unclassified or "Do sprawdzenia".</summary>
    public bool IsLeftOver => !IsClassified || NeedsReview;

    /// <summary>The tier that classified it (<c>globalRule/merchantContains</c>), or <c>unclassified</c>.</summary>
    public string Tier => Predicted is null ? "unclassified" : $"{Predicted.Source.ToString().ToLowerInvariant()}/{Predicted.Reason}";
}

/// <summary>A labelled statement row, parsed and normalized as the importer does.</summary>
public sealed record LabelledRow(int Row, DateOnly Date, RuleInput Input, Guid Expected);

/// <summary>How to run the pipeline (plan 07).</summary>
/// <param name="Rules">The global rules; <see cref="GlobalRules.WithoutMerchants"/> for merchants the list doesn't know.</param>
/// <param name="MemoryMonths">The first months whose labels count as the user's corrections (tier A's examples, C's neighbours); the rest is evaluated.</param>
/// <param name="UseMemory">Run tier A.</param>
/// <param name="Embedder">Run tier C with this model; <c>null</c> to skip it.</param>
public sealed record EvaluationOptions(GlobalRules Rules, int MemoryMonths = 0, bool UseMemory = false, ITextEmbedder? Embedder = null)
{
    public IReadOnlyCollection<MerchantRule> UserRules { get; init; } = [];

    public string? AccountHolder { get; init; }

    public string Name => (UseMemory, Embedder is not null) switch
    {
        (false, false) => "rules",
        (true, false) => "rules + A",
        (false, true) => "rules + C",
        _ => "rules + A + C",
    };
}

/// <summary>Runs the classification pipeline on a statement and compares it with labels (plans 04 and 07).</summary>
public static class Evaluation
{
    /// <summary>Plan 04's measurement: the rules only, every row.</summary>
    public static async Task<EvaluationReport> RunAsync(
        IStatementParser parser,
        Stream statement,
        IReadOnlyList<Label> labels,
        IReadOnlyCollection<MerchantRule> userRules,
        string? accountHolder,
        CancellationToken ct)
    {
        var rows = await LoadAsync(parser, statement, labels, ct);
        return await RunAsync(rows, new EvaluationOptions(GlobalRules.Default) { UserRules = userRules, AccountHolder = accountHolder }, ct);
    }

    /// <summary>The statement's rows that have a label, in file order.</summary>
    public static async Task<IReadOnlyList<LabelledRow>> LoadAsync(IStatementParser parser, Stream statement, IReadOnlyList<Label> labels, CancellationToken ct)
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

        var labelled = new List<LabelledRow>();
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
            labelled.Add(new LabelledRow(i + 1, row.TransactionDate, new RuleInput(kind, row.Amount, key, row.TitleRaw, hash), label.CategoryId));
        }

        return labelled;
    }

    /// <summary>
    /// The rules, then the tiers the options ask for. The labels of the first
    /// <see cref="EvaluationOptions.MemoryMonths"/> months act as the user's corrections and aren't
    /// evaluated; everything after is.
    /// </summary>
    public static async Task<EvaluationReport> RunAsync(IReadOnlyList<LabelledRow> rows, EvaluationOptions options, CancellationToken ct)
    {
        var (memory, evaluated) = Split(rows, options.MemoryMonths);
        var examples = memory.Select(r => new LabelledExample(r.Input.MerchantKey, r.Input.Amount, r.Input.Kind, r.Expected)).ToList();
        var holder = RuleEngine.NameWords(options.AccountHolder);
        var inputs = evaluated.Select(r => r.Input).ToList();
        var matches = inputs.Select(i => RuleEngine.Evaluate(i, options.UserRules, options.Rules, holder)).ToList();

        var clock = Stopwatch.StartNew();
        IClassifier? a = options.UseMemory ? new MemoryClassifier(examples) : null;
        IClassifier? c = null;
        if (options.Embedder is { } embedder)
        {
            var vectors = await embedder.EmbedAsync([.. examples.Select(e => EmbeddingText.For(e.MerchantKey))], ct);
            c = new EmbeddingClassifier(embedder, new AnchorEmbeddings(embedder, options.Rules.Anchors), new InMemoryNeighbours([.. examples.Zip(vectors)]));
        }

        var predicted = await new TierPipeline(a, c).ClassifyAsync(inputs, matches, ct);
        clock.Stop();

        return new EvaluationReport(
            options.Name,
            [.. evaluated.Select((r, i) => new EvaluatedRow(r.Row, r.Date, r.Input.MerchantKey, r.Input.Amount, r.Expected, predicted[i]))],
            clock.Elapsed);
    }

    private static (List<LabelledRow> Memory, List<LabelledRow> Evaluated) Split(IReadOnlyList<LabelledRow> rows, int months)
    {
        if (months <= 0 || rows.Count == 0)
        {
            return ([], [.. rows]);
        }

        var cutoff = rows.Min(r => r.Date).AddMonths(months);
        return ([.. rows.Where(r => r.Date < cutoff)], [.. rows.Where(r => r.Date >= cutoff)]);
    }
}

public sealed class EvaluationReport(string name, IReadOnlyList<EvaluatedRow> rows, TimeSpan tiersElapsed)
{
    public EvaluationReport(IReadOnlyList<EvaluatedRow> rows)
        : this("rules", rows, TimeSpan.Zero)
    {
    }

    public string Name { get; } = name;

    public IReadOnlyList<EvaluatedRow> Rows { get; } = rows;

    /// <summary>Time spent in the tiers after the rules (building the memory, loading and running the model).</summary>
    public TimeSpan TiersElapsed { get; } = tiersElapsed;

    /// <summary>The share of rows the pipeline gave a category (not left unclassified).</summary>
    public double Coverage => Ratio(Rows.Count(r => r.IsClassified), Rows.Count);

    /// <summary>Of the classified rows, the share with the right category.</summary>
    public double Precision => Ratio(Rows.Count(r => r.IsCorrect), Rows.Count(r => r.IsClassified));

    /// <summary>Of the rows classified without needing review, the share with the right category.</summary>
    public double ConfidentPrecision => Ratio(Rows.Count(r => r.IsCorrect && !r.NeedsReview), Rows.Count(r => r.IsClassified && !r.NeedsReview));

    /// <summary>The share classified without needing review: what the user doesn't have to look at.</summary>
    public double ConfidentCoverage => Ratio(Rows.Count(r => r.IsClassified && !r.NeedsReview), Rows.Count);

    public double Accuracy => Ratio(Rows.Count(r => r.IsCorrect), Rows.Count);

    public int ReviewCount => Rows.Count(r => r.NeedsReview);

    /// <summary>Rows left for the user (unclassified or in review) per month of the evaluated period.</summary>
    public double LeftOverPerMonth
    {
        get
        {
            if (Rows.Count == 0)
            {
                return 0;
            }

            var first = Rows.Min(r => r.Date);
            var last = Rows.Max(r => r.Date);
            var months = Math.Max(1, (last.DayNumber - first.DayNumber + 1) / 30.44);
            return Rows.Count(r => r.IsLeftOver) / months;
        }
    }

    /// <summary>Accepting only results at or above <paramref name="threshold"/> (user corrections and rules count as 1): coverage and precision.</summary>
    public (double Coverage, double Precision) AtThreshold(double threshold)
    {
        var accepted = Rows.Where(r => r.Predicted is { } p && p.Confidence >= threshold).ToList();
        return (Ratio(accepted.Count, Rows.Count), Ratio(accepted.Count(r => r.IsCorrect), accepted.Count));
    }

    /// <summary>Per tier: rows, correct ones.</summary>
    public IReadOnlyList<(string Tier, int Rows, int Correct)> Tiers =>
        [.. Rows.GroupBy(r => r.Tier).Select(g => (g.Key, g.Count(), g.Count(r => r.IsCorrect))).OrderByDescending(t => t.Item2)];

    /// <summary>For one source: rows per confidence bucket of 0.1, and how many were right. Calibrated means precision ≈ the bucket.</summary>
    public IReadOnlyList<(double From, int Rows, int Correct)> Reliability(ClassificationSource source) =>
        [.. Rows.Where(r => r.Predicted?.Source == source)
            .GroupBy(r => Math.Min(9, (int)Math.Floor(r.Predicted!.Confidence * 10)) / 10.0)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Count(), g.Count(r => r.IsCorrect)))];

    /// <summary>(expected, predicted) → count, with <c>null</c> for unclassified.</summary>
    public IReadOnlyDictionary<(Guid? Expected, Guid? Predicted), int> Confusion =>
        Rows.GroupBy(r => (r.Expected, r.Predicted?.CategoryId)).ToDictionary(g => g.Key, g => g.Count());

    private static double Ratio(int part, int whole) => whole == 0 ? 0 : (double)part / whole;
}
