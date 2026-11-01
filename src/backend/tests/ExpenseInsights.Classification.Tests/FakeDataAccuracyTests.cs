using System.Text.Json;
using ExpenseInsights.Classification.Embeddings;
using ExpenseInsights.Classification.Eval;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Import.Ing;

namespace ExpenseInsights.Classification.Tests;

/// <summary>Plan 04's acceptance criteria on <c>test-data/fake_ing.csv</c>, measured like the Eval tool does.</summary>
public sealed class FakeDataAccuracyTests
{
    [Fact]
    public async Task Rules_CoverAtLeast90Percent_WithAtLeast97PercentPrecision()
    {
        var report = await EvaluateAsync("fake_ing.csv", "fake_ing.labels.csv");

        Assert.Equal(367, report.Rows.Count);
        Assert.True(report.Coverage >= 0.90, $"Coverage {report.Coverage:P1}");
        Assert.True(report.Precision >= 0.97, $"Precision {report.Precision:P1}");
    }

    [Fact]
    public async Task TheAmbiguousMerchants_AreNotConfidentlyMisclassified()
    {
        var report = await EvaluateAsync("fake_ing.csv", "fake_ing.labels.csv");

        // "9179 KRAKOW ZAKOPIAN" and "P.H.U. ANNA NOWAK": nothing to go on, so left for the user.
        Assert.All(
            report.Rows.Where(r => r.Expected == CategoryCatalog.Other.Id),
            r => Assert.True(!r.IsClassified || r.NeedsReview, $"Row {r.Row}: {r.Tier}"));
    }

    [Theory]
    [InlineData("fake_ing_overlap_a.csv")]
    [InlineData("fake_ing_overlap_b.csv")]
    public async Task OtherFakeExports_ReachTheSameTargets(string csv)
    {
        var report = await EvaluateAsync(csv, csv.Replace(".csv", ".labels.csv", StringComparison.Ordinal));

        Assert.True(report.Coverage >= 0.90, $"Coverage {report.Coverage:P1}");
        Assert.True(report.Precision >= 0.97, $"Precision {report.Precision:P1}");
    }

    [Fact]
    public async Task Memory_AfterOneMonthOfLabels_CoversMerchantsTheRulesDontKnow()
    {
        var rows = await LoadAsync("fake_ing_history.csv", "fake_ing_history.labels.csv");
        var unknown = GlobalRules.Default.WithoutMerchants();

        var rules = await Evaluation.RunAsync(rows, new EvaluationOptions(unknown, MemoryMonths: 1), TestContext.Current.CancellationToken);
        var withMemory = await Evaluation.RunAsync(rows, new EvaluationOptions(unknown, MemoryMonths: 1, UseMemory: true), TestContext.Current.CancellationToken);

        // Plan 07 *Results*: the title keywords alone leave most rows; one month of labels covers nearly all.
        Assert.True(rules.ConfidentCoverage < 0.3, $"Rules alone {rules.ConfidentCoverage:P1}");
        Assert.True(withMemory.ConfidentCoverage >= 0.85, $"Coverage {withMemory.ConfidentCoverage:P1}");
        Assert.True(withMemory.ConfidentPrecision >= 0.98, $"Precision {withMemory.ConfidentPrecision:P1}");
    }

    [Fact]
    public async Task Memory_AddsToTheShippedRules_WithoutConfidentMistakes()
    {
        var rows = await LoadAsync("fake_ing.csv", "fake_ing.labels.csv");

        var report = await Evaluation.RunAsync(rows, new EvaluationOptions(GlobalRules.Default, MemoryMonths: 1, UseMemory: true), TestContext.Current.CancellationToken);

        Assert.True(report.Coverage >= 0.99, $"Coverage {report.Coverage:P1}");
        Assert.Equal(1.0, report.ConfidentPrecision);
    }

    /// <summary>Needs the real model (~135 MB, downloaded into <see cref="E5Model.DefaultDirectory"/>), so it's explicit, like the import benchmarks.</summary>
    [Fact(Explicit = true)]
    public async Task Embeddings_ClassifyLocalMerchants_NobodyLabelled()
    {
        var ct = TestContext.Current.CancellationToken;
        using var http = new HttpClient();
        using var embedder = new E5Embedder(new E5EmbedderOptions(), (dir, token) => E5Model.EnsureAsync(dir, http, token));
        var merchants = await Labels.ReadMerchantsAsync(Path.Combine(AppContext.BaseDirectory, "test-data", "fake_local_merchants.csv"), Categories(), ct);

        var report = await Evaluation.RunAsync(merchants, new EvaluationOptions(GlobalRules.Default.WithoutMerchants(), Embedder: embedder), ct);

        Assert.True(report.ConfidentCoverage >= 0.65, $"Coverage {report.ConfidentCoverage:P1}");
        Assert.True(report.ConfidentPrecision >= 0.95, $"Precision {report.ConfidentPrecision:P1}");
    }

    [Fact]
    public void EveryCategory_HasAPolishName()
    {
        using var pl = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "i18n", "pl.json")));
        var names = pl.RootElement.GetProperty("categories");

        Assert.All(CategoryCatalog.All, c => Assert.False(string.IsNullOrWhiteSpace(names.GetProperty(c.Key).GetString())));
    }

    private static async Task<EvaluationReport> EvaluateAsync(string csv, string labelsFile)
    {
        var ct = TestContext.Current.CancellationToken;
        var labels = await Labels.ReadAsync(Path.Combine(AppContext.BaseDirectory, "test-data", labelsFile), Categories(), ct);
        await using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "test-data", csv));
        return await Evaluation.RunAsync(Parser(), file, labels, [], accountHolder: null, ct);
    }

    private static async Task<IReadOnlyList<LabelledRow>> LoadAsync(string csv, string labelsFile)
    {
        var ct = TestContext.Current.CancellationToken;
        var labels = await Labels.ReadAsync(Path.Combine(AppContext.BaseDirectory, "test-data", labelsFile), Categories(), ct);
        await using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "test-data", csv));
        return await Evaluation.LoadAsync(Parser(), file, labels, ct);
    }

    private static IReadOnlyDictionary<string, Guid> Categories() => Labels.CategoryNames(Path.Combine(AppContext.BaseDirectory, "i18n", "pl.json"));

    private static IngCsvParser Parser() => new(new IngParserOptions { PaymentProcessors = IngParserOptions.DefaultPaymentProcessors });
}
