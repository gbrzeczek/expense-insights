using System.Text;
using System.Text.Json;

namespace ExpenseInsights.Classification.Embeddings;

/// <summary>
/// The XLM-RoBERTa tokenizer of <c>multilingual-e5-small</c>: a SentencePiece Unigram model read
/// from the Hugging Face <c>tokenizer.json</c>. NFKC, whitespace split with a <c>▁</c> prefix per
/// word, then the most likely segmentation of each word (Viterbi); <c>&lt;s&gt; … &lt;/s&gt;</c>
/// around it. Enough for short merchant strings; checked against the reference implementation in
/// the tests.
/// </summary>
public sealed class UnigramTokenizer
{
    public const int BosId = 0;
    public const int PadId = 1;
    public const int EosId = 2;

    private const char Space = '▁';

    /// <summary>The reference penalty for an unknown piece (<c>kUnkPenalty</c>).</summary>
    private const double UnknownPenalty = 10;

    private readonly Dictionary<string, (int Id, double Score)> _vocab;
    private readonly Dictionary<string, (int Id, double Score)>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly int _unknownId;
    private readonly double _unknownScore;
    private readonly int _maxPieceLength;

    private UnigramTokenizer(Dictionary<string, (int Id, double Score)> vocab, int unknownId)
    {
        _vocab = vocab;
        _lookup = vocab.GetAlternateLookup<ReadOnlySpan<char>>();
        _unknownId = unknownId;
        _unknownScore = vocab.Values.Min(v => v.Score) - UnknownPenalty;
        _maxPieceLength = vocab.Keys.Max(k => k.Length);
    }

    public static UnigramTokenizer Load(string tokenizerJsonPath)
    {
        using var stream = File.OpenRead(tokenizerJsonPath);
        using var document = JsonDocument.Parse(stream);
        var model = document.RootElement.GetProperty("model");
        if (model.GetProperty("type").GetString() != "Unigram")
        {
            throw new InvalidDataException($"{tokenizerJsonPath}: not a Unigram tokenizer.");
        }

        var vocab = new Dictionary<string, (int, double)>(StringComparer.Ordinal);
        var id = 0;
        foreach (var entry in model.GetProperty("vocab").EnumerateArray())
        {
            // Special tokens (<s>, <pad>, …) never come out of a segmentation; only pieces do.
            var piece = entry[0].GetString()!;
            if (!(piece.StartsWith('<') && piece.EndsWith('>')))
            {
                vocab.TryAdd(piece, (id, entry[1].GetDouble()));
            }

            id++;
        }

        return new UnigramTokenizer(vocab, model.GetProperty("unk_id").GetInt32());
    }

    /// <summary>The token ids with <c>&lt;s&gt;</c> and <c>&lt;/s&gt;</c>, cut to <paramref name="maxTokens"/> in all.</summary>
    public int[] Encode(string text, int maxTokens)
    {
        var ids = new List<int> { BosId };
        var normalized = text.Normalize(NormalizationForm.FormKC);
        foreach (var word in normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            Segment(Space + word, ids);
            if (ids.Count >= maxTokens - 1)
            {
                break;
            }
        }

        if (ids.Count > maxTokens - 1)
        {
            ids.RemoveRange(maxTokens - 1, ids.Count - (maxTokens - 1));
        }

        ids.Add(EosId);
        return [.. ids];
    }

    /// <summary>Viterbi over the pieces in the vocabulary; a character no piece covers is unknown.</summary>
    private void Segment(string word, List<int> ids)
    {
        var best = new double[word.Length + 1];
        var from = new int[word.Length + 1];
        var token = new int[word.Length + 1];
        Array.Fill(best, double.NegativeInfinity);
        best[0] = 0;
        for (var end = 1; end <= word.Length; end++)
        {
            for (var start = Math.Max(0, end - _maxPieceLength); start < end; start++)
            {
                if (double.IsNegativeInfinity(best[start]) || !_lookup.TryGetValue(word.AsSpan(start, end - start), out var piece))
                {
                    continue;
                }

                if (best[start] + piece.Score > best[end])
                {
                    (best[end], from[end], token[end]) = (best[start] + piece.Score, start, piece.Id);
                }
            }

            // One character as unknown, if nothing better reaches here (a surrogate pair stays whole).
            var length = end >= 2 && char.IsSurrogatePair(word[end - 2], word[end - 1]) ? 2 : 1;
            if (double.IsNegativeInfinity(best[end]) && !double.IsNegativeInfinity(best[end - length]))
            {
                (best[end], from[end], token[end]) = (best[end - length] + _unknownScore, end - length, _unknownId);
            }
        }

        var pieces = new List<int>();
        for (var at = word.Length; at > 0; at = from[at])
        {
            // Consecutive unknown characters are one unknown token, as in the reference.
            if (!(token[at] == _unknownId && pieces.Count > 0 && pieces[^1] == _unknownId))
            {
                pieces.Add(token[at]);
            }
        }

        pieces.Reverse();
        ids.AddRange(pieces);
    }

    /// <summary>For tests: the number of pieces in the vocabulary.</summary>
    internal int Count => _vocab.Count;
}
