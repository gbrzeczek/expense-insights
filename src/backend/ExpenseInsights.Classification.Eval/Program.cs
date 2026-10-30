using System.Globalization;
using ExpenseInsights.Classification.Eval;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Import.Ing;

// Measures the classification pipeline against labels (plan 04).
//
//   dotnet run --project ExpenseInsights.Classification.Eval
//       fake data: test-data/fake_ing.csv vs fake_ing.labels.csv
//   dotnet run --project ExpenseInsights.Classification.Eval -- --csv ~/private/ing.csv --labels ~/private/ing.labels.csv
//       a real export with the owner's corrections, kept outside the repo (never commit real data)
//
// Options: --holder "Jan Kowalski" (the account holder's name, as the app knows it from sign-in),
// --pl path/to/pl.json (category names), --errors (list every wrong or unclassified row).
var options = ParseArgs(args);
var root = FindRepoRoot();
var csv = options.GetValueOrDefault("csv") ?? Path.Combine(root, "test-data", "fake_ing.csv");
var labelsPath = options.GetValueOrDefault("labels") ?? Path.ChangeExtension(csv, ".labels.csv");
var pl = options.GetValueOrDefault("pl") ?? Path.Combine(root, "src", "frontend", "public", "i18n", "pl.json");

var categories = Labels.CategoryNames(pl);
var names = CategoryCatalog.All.ToDictionary(c => c.Id, c => categories.FirstOrDefault(n => n.Value == c.Id && n.Key != c.Key).Key ?? c.Key);
var labels = await Labels.ReadAsync(labelsPath, categories, CancellationToken.None);
await using var file = File.OpenRead(csv);
var parser = new IngCsvParser(new IngParserOptions { PaymentProcessors = IngParserOptions.DefaultPaymentProcessors });
var report = await Evaluation.RunAsync(parser, file, labels, [], options.GetValueOrDefault("holder"), CancellationToken.None);

string Name(Guid? id) => id is { } value ? names.GetValueOrDefault(value, value.ToString()) : "(brak)";
string Percent(double value) => value.ToString("P1", CultureInfo.InvariantCulture);

Console.WriteLine($"{Path.GetFileName(csv)}: {report.Rows.Count} labelled rows");
Console.WriteLine($"  coverage   {Percent(report.Coverage),8}   (classified, not left unclassified; target ≥ 90%)");
Console.WriteLine($"  precision  {Percent(report.Precision),8}   (right category among classified; target ≥ 97%)");
Console.WriteLine($"    without review {Percent(report.ConfidentPrecision),8}");
Console.WriteLine($"  accuracy   {Percent(report.Accuracy),8}   (right category among all)");
Console.WriteLine($"  to review  {report.ReviewCount,8}   (\"Do sprawdzenia\")");
Console.WriteLine();
Console.WriteLine("Per tier (rows, correct):");
foreach (var (tier, rows, correct) in report.Tiers)
{
    Console.WriteLine($"  {tier,-36} {rows,5} {correct,5}");
}

Console.WriteLine();
Console.WriteLine("Confusion (expected → predicted, mistakes and unclassified only):");
foreach (var ((expected, predicted), count) in report.Confusion.Where(c => c.Key.Expected != c.Key.Predicted).OrderByDescending(c => c.Value))
{
    Console.WriteLine($"  {count,5}  {Name(expected)} → {Name(predicted)}");
}

if (options.ContainsKey("errors"))
{
    Console.WriteLine();
    foreach (var row in report.Rows.Where(r => !r.IsCorrect))
    {
        Console.WriteLine($"  row {row.Row,5}  {row.MerchantKey,-40} {Name(row.Expected)} → {Name(row.Predicted?.CategoryId)} ({row.Tier})");
    }
}

return 0;

static Dictionary<string, string?> ParseArgs(string[] args)
{
    var options = new Dictionary<string, string?>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unexpected argument '{args[i]}'.");
        }

        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
        options[args[i][2..]] = hasValue ? args[++i] : null;
    }

    return options;
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "test-data")) && File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            return dir.FullName;
        }
    }

    return Directory.GetCurrentDirectory();
}
