using System.Text.Json;
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

        Assert.Equal(335, report.Rows.Count);
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
    public void EveryCategory_HasAPolishName()
    {
        using var pl = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "i18n", "pl.json")));
        var names = pl.RootElement.GetProperty("categories");

        Assert.All(CategoryCatalog.All, c => Assert.False(string.IsNullOrWhiteSpace(names.GetProperty(c.Key).GetString())));
    }

    private static async Task<EvaluationReport> EvaluateAsync(string csv, string labelsFile)
    {
        var ct = TestContext.Current.CancellationToken;
        var categories = Labels.CategoryNames(Path.Combine(AppContext.BaseDirectory, "i18n", "pl.json"));
        var labels = await Labels.ReadAsync(Path.Combine(AppContext.BaseDirectory, "test-data", labelsFile), categories, ct);
        await using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "test-data", csv));
        var parser = new IngCsvParser(new IngParserOptions { PaymentProcessors = IngParserOptions.DefaultPaymentProcessors });
        return await Evaluation.RunAsync(parser, file, labels, [], accountHolder: null, ct);
    }
}
