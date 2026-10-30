using System.Text.Json;
using ExpenseInsights.Core.Classification;
using ExpenseInsights.Core.Import;

namespace ExpenseInsights.Classification;

/// <summary>Which transactions a global rule applies to, by the sign of the amount.</summary>
public enum Direction
{
    Out,
    In,
    Any,
}

public sealed record GlobalRule(RuleMatchType MatchType, string Pattern, Guid CategoryId, Direction Direction)
{
    public bool Applies(decimal amount) => Direction switch
    {
        Direction.Out => amount < 0,
        Direction.In => amount > 0,
        _ => true,
    };
}

/// <summary>
/// The rules every user gets, from the embedded <c>global-rules.json</c> (a hand-maintained list
/// of Polish merchants and chains). Not stored in the database: a new version ships with the app.
/// </summary>
public sealed class GlobalRules
{
    private GlobalRules(IReadOnlyList<GlobalRule> rules)
    {
        Rules = rules;
        Merchants = [.. rules
            .Where(r => r.MatchType is RuleMatchType.Exact or RuleMatchType.Contains)
            .Select(r => (r, TrigramSimilarity.Trigrams(r.Pattern)))];
    }

    public static GlobalRules Default { get; } = Load();

    public IReadOnlyList<GlobalRule> Rules { get; }

    /// <summary>The merchant patterns with their trigrams, for similarity matching.</summary>
    internal IReadOnlyList<(GlobalRule Rule, HashSet<string> Trigrams)> Merchants { get; }

    public static GlobalRules Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var rules = new List<GlobalRule>();
        foreach (var category in document.RootElement.GetProperty("categories").EnumerateObject())
        {
            var catalog = CategoryCatalog.ByKey(category.Name);
            var direction = category.Value.TryGetProperty("direction", out var d)
                ? Enum.Parse<Direction>(d.GetString()!, ignoreCase: true)
                : catalog.Kind == CategoryKind.Income ? Direction.In : Direction.Out;
            foreach (var (property, matchType) in new[] { ("exact", RuleMatchType.Exact), ("contains", RuleMatchType.Contains), ("title", RuleMatchType.TitleKeyword) })
            {
                if (!category.Value.TryGetProperty(property, out var patterns))
                {
                    continue;
                }

                foreach (var pattern in patterns.EnumerateArray())
                {
                    var value = pattern.GetString()!;
                    if (value != MerchantNormalizer.Fold(value).Trim())
                    {
                        throw new InvalidOperationException($"Global rule pattern '{value}' must be lowercase without diacritics.");
                    }

                    rules.Add(new GlobalRule(matchType, value, catalog.Id, direction));
                }
            }
        }

        return new GlobalRules(rules);
    }

    private static GlobalRules Load()
    {
        using var stream = typeof(GlobalRules).Assembly.GetManifestResourceStream("ExpenseInsights.Classification.global-rules.json")
            ?? throw new InvalidOperationException("The embedded global-rules.json is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
