using System.Globalization;
using ExpenseInsights.Classification;
using ExpenseInsights.Classification.Embeddings;
using ExpenseInsights.Classification.Eval;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Import.Ing;

// Measures the classification pipeline against labels (plans 04 and 07).
//
//   dotnet run --project ExpenseInsights.Classification.Eval
//       fake data: test-data/fake_ing.csv vs fake_ing.labels.csv
//   dotnet run --project ExpenseInsights.Classification.Eval -- --csv ~/private/ing.csv --labels ~/private/ing.labels.csv
//       a real export with the owner's corrections, kept outside the repo (never commit real data)
//
// Options: --holder "Jan Kowalski" (the account holder's name, as the app knows it from sign-in),
// --pl path/to/pl.json (category names), --errors (list every wrong or unclassified row),
// --memory-months 1 (the first months' labels act as the user's corrections for tiers A and C),
// --no-embeddings (skip tier C), --model-dir (where multilingual-e5-small is, downloaded if missing),
// --leftover (what is still left for the user after rules + A + C), --merchants (the local merchants list).
var options = ParseArgs(args);
var root = FindRepoRoot();
var csv = options.GetValueOrDefault("csv") ?? Path.Combine(root, "test-data", "fake_ing.csv");
var labelsPath = options.GetValueOrDefault("labels") ?? Path.ChangeExtension(csv, ".labels.csv");
var pl = options.GetValueOrDefault("pl") ?? Path.Combine(root, "src", "frontend", "public", "i18n", "pl.json");
var memoryMonths = int.Parse(options.GetValueOrDefault("memory-months") ?? "1", CultureInfo.InvariantCulture);
var holder = options.GetValueOrDefault("holder");
double[] thresholds = [0.3, 0.5, 0.6, 0.7, 0.8, 0.9];

var categories = Labels.CategoryNames(pl);
var names = CategoryCatalog.All.ToDictionary(c => c.Id, c => categories.FirstOrDefault(n => n.Value == c.Id && !string.Equals(n.Key, c.Key, StringComparison.Ordinal)).Key ?? c.Key);
var labels = await Labels.ReadAsync(labelsPath, categories, CancellationToken.None);
await using var file = File.OpenRead(csv);
var parser = new IngCsvParser(new IngParserOptions { PaymentProcessors = IngParserOptions.DefaultPaymentProcessors });
var rows = await Evaluation.LoadAsync(parser, file, labels, CancellationToken.None);
var report = await Evaluation.RunAsync(rows, new EvaluationOptions(GlobalRules.Default) { AccountHolder = holder }, CancellationToken.None);

string Name(Guid? id) => id is { } value ? names.GetValueOrDefault(value, value.ToString()) : "(brak)";
string Percent(double value) => value.ToString("P1", CultureInfo.InvariantCulture);
string Number(double value, string format = "0.0") => value.ToString(format, CultureInfo.InvariantCulture);

Console.WriteLine($"{Path.GetFileName(csv)}: {report.Rows.Count} labelled rows");
Console.WriteLine($"  coverage   {Percent(report.Coverage),8}   (classified, not left unclassified; target ≥ 90%)");
Console.WriteLine($"  precision  {Percent(report.Precision),8}   (right category among classified; target ≥ 97%)");
Console.WriteLine($"    without review {Percent(report.ConfidentPrecision),8}");
Console.WriteLine($"  accuracy   {Percent(report.Accuracy),8}   (right category among all)");
Console.WriteLine($"  to review  {report.ReviewCount,8}   (\"Do sprawdzenia\")");
Console.WriteLine();
Console.WriteLine("Per tier (rows, correct):");
foreach (var (tier, count, correct) in report.Tiers)
{
    Console.WriteLine($"  {tier,-36} {count,5} {correct,5}");
}

Console.WriteLine();
Console.WriteLine("Confusion (expected → predicted, mistakes and unclassified only):");
foreach (var ((expected, predicted), count) in report.Confusion.Where(c => c.Key.Expected != c.Key.Predicted).OrderByDescending(c => c.Value))
{
    Console.WriteLine($"  {count,5}  {Name(expected)} → {Name(predicted)}");
}

if (options.ContainsKey("errors"))
{
    PrintErrors(report);
}

// Plan 07: the tiers after the rules, with the first months' labels as the user's corrections.
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var embedder = options.ContainsKey("no-embeddings")
    ? null
    : new E5Embedder(new E5EmbedderOptions { ModelDirectory = options.GetValueOrDefault("model-dir") }, (dir, ct) => E5Model.EnsureAsync(dir, http, ct));
var memoryRows = memoryMonths <= 0 ? 0 : rows.Count(r => r.Date < rows.Min(x => x.Date).AddMonths(memoryMonths));
var workingSetBefore = Environment.WorkingSet;

Console.WriteLine();
Console.WriteLine($"Tiers (plan 07): the first {memoryMonths} month(s) of labels ({memoryRows} rows) are the user's corrections; {rows.Count - memoryRows} rows evaluated.");
var scenarios = new (string Title, GlobalRules Rules)[]
{
    ("Global rules as shipped", GlobalRules.Default),
    ("Merchants unknown to the rules (title keywords only)", GlobalRules.Default.WithoutMerchants()),
};
var results = new List<(string Scenario, EvaluationReport Report)>();
foreach (var (title, rules) in scenarios)
{
    Console.WriteLine();
    Console.WriteLine($"{title}:");
    Console.WriteLine($"  {"",-14} {"coverage",9} {"precision",9} {"no review",9} {"its prec.",9} {"review",6} {"left/mo",7} {"tiers ms",8}");
    foreach (var (useMemory, useEmbeddings) in new[] { (false, false), (true, false), (false, true), (true, true) })
    {
        if (useEmbeddings && embedder is null)
        {
            continue;
        }

        var run = await Evaluation.RunAsync(rows, new EvaluationOptions(rules, memoryMonths, useMemory, useEmbeddings ? embedder : null) { AccountHolder = holder }, CancellationToken.None);
        results.Add((title, run));
        Console.WriteLine($"  {run.Name,-14} {Percent(run.Coverage),9} {Percent(run.Precision),9} {Percent(run.ConfidentCoverage),9} {Percent(run.ConfidentPrecision),9} {run.ReviewCount,6} {Number(run.LeftOverPerMonth),7} {run.TiersElapsed.TotalMilliseconds,8:0}");
    }

    var best = results[^1].Report;
    Console.WriteLine($"  {best.Name} accepting only confidence ≥ t (coverage / precision):");
    Console.WriteLine("    " + string.Join("   ", thresholds.Select(t => best.AtThreshold(t) is var (cov, prec) ? $"{Number(t, "0.0")}: {Percent(cov)} / {Percent(prec)}" : "")));
}

Console.WriteLine();
Console.WriteLine("Reliability, merchants unknown to the rules, rules + A + C (confidence bucket: rows, precision):");
var unknown = results[^1].Report;
foreach (var source in new[] { ClassificationSource.Similarity, ClassificationSource.Embedding })
{
    var buckets = unknown.Reliability(source);
    Console.WriteLine($"  {source,-10} " + (buckets.Count == 0 ? "–" : string.Join("  ", buckets.Select(b => $"{Number(b.From, "0.0")}: {b.Rows} {Percent((double)b.Correct / b.Rows)}"))));
}

// The category anchors on local shops nobody has labelled and no rule knows by name.
var localPath = options.GetValueOrDefault("merchants") ?? Path.Combine(root, "test-data", "fake_local_merchants.csv");
if (embedder is not null && File.Exists(localPath))
{
    var local = await Labels.ReadMerchantsAsync(localPath, categories, CancellationToken.None);
    Console.WriteLine();
    Console.WriteLine($"Category anchors on {local.Count} local merchants ({Path.GetFileName(localPath)}), no labels:");
    Console.WriteLine($"  {"",-34} {"coverage",9} {"precision",9} {"no review",9} {"its prec.",9} {"review",6} {"tiers ms",8}");
    foreach (var (title, rules) in new[] { ("rules as shipped", GlobalRules.Default), ("rules as shipped + C", GlobalRules.Default), ("C alone (title keywords only)", GlobalRules.Default.WithoutMerchants()) })
    {
        var run = await Evaluation.RunAsync(local, new EvaluationOptions(rules, Embedder: title.Contains('C', StringComparison.Ordinal) ? embedder : null), CancellationToken.None);
        Console.WriteLine($"  {title,-34} {Percent(run.Coverage),9} {Percent(run.Precision),9} {Percent(run.ConfidentCoverage),9} {Percent(run.ConfidentPrecision),9} {run.ReviewCount,6} {run.TiersElapsed.TotalMilliseconds,8:0}");
        if (options.ContainsKey("errors") && run.Name == "rules + C")
        {
            PrintErrors(run);
        }

        if (title.StartsWith('C'))
        {
            var buckets = run.Reliability(ClassificationSource.Embedding);
            Console.WriteLine("    reliability: " + string.Join("  ", buckets.Select(b => $"{Number(b.From, "0.0")}: {b.Rows} {Percent((double)b.Correct / b.Rows)}")));
        }
    }
}

if (embedder is not null)
{
    Console.WriteLine();
    Console.WriteLine($"Memory: working set {Environment.WorkingSet / 1e6:0} MB with the model loaded ({workingSetBefore / 1e6:0} MB before){PeakRss()}; model loads: {embedder.Loads}.");
    embedder.Unload();
    Console.WriteLine($"  after unloading: {Environment.WorkingSet / 1e6:0} MB.");
}

if (options.ContainsKey("leftover"))
{
    var shipped = results.Last(r => r.Scenario == scenarios[0].Title).Report;
    foreach (var (title, run) in new[] { (scenarios[0].Title, shipped), (scenarios[1].Title, unknown) })
    {
        Console.WriteLine();
        Console.WriteLine($"Left over after {run.Name}, {title.ToLowerInvariant()}: {Number(run.LeftOverPerMonth)} a month");
        foreach (var group in run.Rows.Where(r => r.IsLeftOver).GroupBy(r => r.MerchantKey).OrderByDescending(g => g.Count()))
        {
            var guess = group.First().Predicted;
            Console.WriteLine($"  {group.Count(),4}  {group.Key,-36} expected {Name(group.First().Expected)}; {(guess is null ? "unclassified" : $"guess {Name(guess.CategoryId)} {Number(guess.Confidence, "0.00")} ({guess.Reason} {guess.ReasonValue})")}");
        }
    }
}

if (options.ContainsKey("errors"))
{
    Console.WriteLine();
    Console.WriteLine($"Mistakes of {unknown.Name}, merchants unknown to the rules:");
    PrintErrors(unknown);
}

return 0;

void PrintErrors(EvaluationReport run)
{
    Console.WriteLine();
    foreach (var row in run.Rows.Where(r => !r.IsCorrect))
    {
        var detail = row.Predicted is { } p ? $"{row.Tier} {Number(p.Confidence, "0.00")} {p.ReasonValue}" : row.Tier;
        Console.WriteLine($"  row {row.Row,5}  {row.MerchantKey,-40} {Name(row.Expected)} → {Name(row.Predicted?.CategoryId)} ({detail})");
    }
}

// The process's peak resident memory, where the OS reports it (Linux: VmHWM).
static string PeakRss()
{
    const string status = "/proc/self/status";
    var line = File.Exists(status) ? File.ReadLines(status).FirstOrDefault(l => l.StartsWith("VmHWM:", StringComparison.Ordinal)) : null;
    return line is null ? "" : $", peak {line[6..].Trim()}";
}

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
