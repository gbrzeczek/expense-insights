namespace ExpenseInsights.Core.Import;

/// <summary>
/// Trigram similarity computed the way Postgres <c>pg_trgm</c> does, for comparing a handful of
/// candidates in memory.
/// </summary>
public static class TrigramSimilarity
{
    /// <summary>The <c>pg_trgm.similarity_threshold</c> default.</summary>
    public const double DefaultThreshold = 0.3;

    public static double Similarity(string a, string b)
    {
        var left = Trigrams(a);
        var right = Trigrams(b);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var shared = left.Count(right.Contains);
        return (double)shared / (left.Count + right.Count - shared);
    }

    private static HashSet<string> Trigrams(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new string([.. text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ')]);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var padded = $"  {word} ";
            for (var i = 0; i + 3 <= padded.Length; i++)
            {
                result.Add(padded.Substring(i, 3));
            }
        }

        return result;
    }
}
