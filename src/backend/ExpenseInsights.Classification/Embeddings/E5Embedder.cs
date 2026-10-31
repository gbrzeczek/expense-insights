using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ExpenseInsights.Classification.Embeddings;

/// <summary>Turns short texts into unit vectors whose dot product is their cosine similarity.</summary>
public interface ITextEmbedder
{
    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

public sealed class E5EmbedderOptions
{
    /// <summary>Where the model files are (and are downloaded to); <see cref="E5Model.DefaultDirectory"/> if unset.</summary>
    public string? ModelDirectory { get; set; }

    /// <summary>The model is unloaded after this long without a batch, to give the RAM back.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>CPU threads for one batch; the i3-6100T has 2 cores.</summary>
    public int Threads { get; set; } = 2;

    /// <summary>Texts per model run.</summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>Merchant strings are short; anything longer is cut.</summary>
    public int MaxTokens { get; set; } = 48;
}

/// <summary>
/// <c>multilingual-e5-small</c> on ONNX Runtime (tier C, plan 07). Loaded lazily on the first batch,
/// shared by everyone, and unloaded after <see cref="E5EmbedderOptions.IdleTimeout"/> so the
/// ~250 MB it needs aren't held between imports. Batches only: one transaction at a time would
/// spend most of the time in overhead.
/// </summary>
public sealed partial class E5Embedder : ITextEmbedder, IDisposable
{
    private readonly E5EmbedderOptions _options;
    private readonly Func<string, CancellationToken, Task>? _download;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Timer _idle;
    private Loaded? _loaded;

    /// <param name="options">Where the model is, and how to run it.</param>
    /// <param name="download">Fetches missing model files into the directory; <c>null</c> to require them.</param>
    public E5Embedder(E5EmbedderOptions options, Func<string, CancellationToken, Task>? download = null)
    {
        _options = options;
        _download = download;
        _idle = new Timer(_ => Unload(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string Directory => _options.ModelDirectory ?? E5Model.DefaultDirectory;

    /// <summary>Whether the model is in memory right now.</summary>
    public bool IsLoaded => _loaded is not null;

    /// <summary>How many times it was loaded (each load costs ~1 s).</summary>
    public int Loads { get; private set; }

    /// <summary>Whether the model files are there, or can be downloaded.</summary>
    public bool IsAvailable => _download is not null || E5Model.IsPresent(Directory);

    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        await _lock.WaitAsync(ct);
        try
        {
            _idle.Change(Timeout.Infinite, Timeout.Infinite);
            var loaded = _loaded ??= await LoadAsync(ct);
            var result = new float[texts.Count][];
            for (var start = 0; start < texts.Count; start += _options.BatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batch = texts.Skip(start).Take(_options.BatchSize).ToList();
                Embed(loaded, batch).CopyTo(result, start);
            }

            return result;
        }
        finally
        {
            _idle.Change(_options.IdleTimeout, Timeout.InfiniteTimeSpan);
            _lock.Release();
        }
    }

    /// <summary>Frees the model now; the next batch loads it again.</summary>
    public void Unload()
    {
        if (!_lock.Wait(0))
        {
            // A batch is running; it re-arms the timer when it's done.
            return;
        }

        try
        {
            if (_loaded is null)
            {
                return;
            }

            _loaded.Session.Dispose();
            _loaded = null;
        }
        finally
        {
            _lock.Release();
        }

        // The tokenizer's vocabulary is ~30 MB of managed strings: give it back to the OS too.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        // ONNX Runtime's buffers go back to glibc, which keeps them unless asked to return them.
        if (OperatingSystem.IsLinux())
        {
            _ = MallocTrim(0);
        }
    }

    [LibraryImport("libc", EntryPoint = "malloc_trim")]
    private static partial int MallocTrim(nuint pad);

    public void Dispose()
    {
        _idle.Dispose();
        _loaded?.Session.Dispose();
        _loaded = null;
        _lock.Dispose();
    }

    private async Task<Loaded> LoadAsync(CancellationToken ct)
    {
        if (!E5Model.IsPresent(Directory))
        {
            if (_download is null)
            {
                throw new FileNotFoundException($"The embedding model isn't in {Directory}.");
            }

            await _download(Directory, ct);
        }

        using var options = new SessionOptions
        {
            IntraOpNumThreads = _options.Threads,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        var session = new InferenceSession(Path.Combine(Directory, E5Model.Model.Name), options);
        var tokenizer = UnigramTokenizer.Load(Path.Combine(Directory, E5Model.Tokenizer.Name));
        Loads++;
        return new Loaded(session, tokenizer);
    }

    private float[][] Embed(Loaded loaded, List<string> texts)
    {
        var encoded = texts.Select(t => loaded.Tokenizer.Encode(t, _options.MaxTokens)).ToList();
        var length = encoded.Max(e => e.Length);
        var ids = new DenseTensor<long>([texts.Count, length]);
        var mask = new DenseTensor<long>([texts.Count, length]);
        var types = new DenseTensor<long>([texts.Count, length]);
        for (var row = 0; row < encoded.Count; row++)
        {
            for (var i = 0; i < length; i++)
            {
                ids[row, i] = i < encoded[row].Length ? encoded[row][i] : UnigramTokenizer.PadId;
                mask[row, i] = i < encoded[row].Length ? 1 : 0;
            }
        }

        using var outputs = loaded.Session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", ids),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", types),
        ]);
        var hidden = outputs[0].AsTensor<float>();
        var dimensions = hidden.Dimensions[2];

        // Mean over the real tokens, then unit length (as the model card says).
        var result = new float[texts.Count][];
        for (var row = 0; row < encoded.Count; row++)
        {
            var vector = new float[dimensions];
            var tokens = encoded[row].Length;
            for (var i = 0; i < tokens; i++)
            {
                for (var d = 0; d < dimensions; d++)
                {
                    vector[d] += hidden[row, i, d];
                }
            }

            var norm = MathF.Sqrt(vector.Sum(v => v * v));
            for (var d = 0; d < dimensions; d++)
            {
                vector[d] /= norm;
            }

            result[row] = vector;
        }

        return result;
    }

    private sealed record Loaded(InferenceSession Session, UnigramTokenizer Tokenizer);
}
