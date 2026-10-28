using ExpenseInsights.Core.Import;

namespace ExpenseInsights.Core.Tests.Import;

public sealed class TrigramSimilarityTests
{
    [Fact]
    public void MatchesPgTrgm_OnItsDocumentedExample()
    {
        // SELECT similarity('word', 'two words') = 0.36363637 (Postgres docs).
        Assert.Equal(0.363636, TrigramSimilarity.Similarity("word", "two words"), 5);
    }

    [Theory]
    [InlineData("ZABKA Z5017 K.1 KRAKOW POL", "zabka z5017 k.1 krakow pol", 1.0)]
    [InlineData("abc", "xyz", 0.0)]
    [InlineData("", "abc", 0.0)]
    public void Similarity_IsCaseInsensitive_AndBounded(string a, string b, double expected)
    {
        Assert.Equal(expected, TrigramSimilarity.Similarity(a, b), 5);
    }
}
