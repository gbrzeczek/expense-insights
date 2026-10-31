using ExpenseInsights.Classification;
using ExpenseInsights.Classification.Embeddings;
using ExpenseInsights.Classification.Tiers;

namespace ExpenseInsights.Infrastructure.Classification;

/// <summary>The <c>Classification</c> configuration section: which tiers run after the rules (plan 07).</summary>
public sealed class ClassificationOptions
{
    public const string Section = "Classification";

    /// <summary>Tier A, the memory of the user's labels. Pure and cheap.</summary>
    public bool Memory { get; set; } = true;

    public EmbeddingOptions Embeddings { get; set; } = new();
}

public sealed class EmbeddingOptions
{
    /// <summary>Tier C, the local embedding model. Off in the API tests, which would otherwise download it.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where the model files are; <see cref="E5Model.DefaultDirectory"/> if unset (the production image sets <c>/app/models</c>).</summary>
    public string? ModelDirectory { get; set; }

    /// <summary>Download missing model files on first use (the production image has them built in).</summary>
    public bool Download { get; set; } = true;

    /// <summary>How long the model stays in memory after the last batch.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>CPU threads for the model.</summary>
    public int Threads { get; set; } = 2;
}

/// <summary>The tiers the pipeline runs after the rules, shared by every classification run.</summary>
/// <param name="Memory">Whether tier A runs.</param>
/// <param name="Embedder">Tier C's model, or <c>null</c> when it's off.</param>
/// <param name="Anchors">The category anchors' embeddings, with <paramref name="Embedder"/>.</param>
public sealed record ClassificationTiers(bool Memory, ITextEmbedder? Embedder, AnchorEmbeddings? Anchors) : IDisposable
{
    /// <summary>The rules and tier A, without the model: the default, and what the tests use.</summary>
    public static ClassificationTiers RulesAndMemory { get; } = new(true, null, null);

    /// <summary>Which model made an embedding; embeddings of another model are recomputed.</summary>
    public const string Model = $"{E5Model.Repository}@{E5Model.Revision}";

    public static ClassificationTiers Create(ClassificationOptions options)
    {
        if (!options.Embeddings.Enabled)
        {
            return new(options.Memory, null, null);
        }

        var embedding = options.Embeddings;
        var embedder = new E5Embedder(
            new E5EmbedderOptions { ModelDirectory = embedding.ModelDirectory, IdleTimeout = embedding.IdleTimeout, Threads = embedding.Threads },
            embedding.Download ? DownloadAsync : null);
        return new(options.Memory, embedder, new AnchorEmbeddings(embedder, GlobalRules.Default.Anchors));
    }

    public void Dispose()
    {
        (Embedder as IDisposable)?.Dispose();
        Anchors?.Dispose();
    }

    private static async Task DownloadAsync(string directory, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        await E5Model.EnsureAsync(directory, http, ct);
    }
}
