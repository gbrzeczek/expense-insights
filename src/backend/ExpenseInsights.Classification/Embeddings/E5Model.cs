using System.Security.Cryptography;

namespace ExpenseInsights.Classification.Embeddings;

/// <summary>A file of the embedding model, pinned by its SHA-256.</summary>
public sealed record ModelFile(string Name, string Sha256, long Size);

/// <summary>
/// <c>intfloat/multilingual-e5-small</c> (MIT licence): the int8 ONNX export from the model's own
/// repository, at a pinned revision. Not committed: downloaded on first use and checked against the
/// hashes below (plan 07).
/// </summary>
public static class E5Model
{
    public const string Repository = "intfloat/multilingual-e5-small";
    public const string Revision = "614241f622f53c4eeff9890bdc4f31cfecc418b3";
    public const int Dimensions = 384;

    public static readonly ModelFile Model = new("model_qint8_avx512_vnni.onnx", "dd476dd0c2514e9b9be83aeb3853fac0763e0bdf4a71645407587d77c48a2d88", 118_346_824);
    public static readonly ModelFile Tokenizer = new("tokenizer.json", "0b44a9d7b51c3c62626640cda0e2c2f70fdacdc25bbbd68038369d14ebdf4c39", 17_082_730);

    public static IReadOnlyList<ModelFile> Files { get; } = [Model, Tokenizer];

    /// <summary>The default directory outside the repository: <c>~/.cache/expense-insights/models/multilingual-e5-small</c>.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "expense-insights", "models", "multilingual-e5-small");

    public static Uri Url(ModelFile file) => new($"https://huggingface.co/{Repository}/resolve/{Revision}/onnx/{file.Name}");

    /// <summary>Whether every file is there with the expected size (the hash is checked when downloading).</summary>
    public static bool IsPresent(string directory) =>
        Files.All(f => new FileInfo(Path.Combine(directory, f.Name)) is { Exists: true } info && info.Length == f.Size);

    /// <summary>Downloads the missing files into <paramref name="directory"/>, verifying each one before it's moved into place.</summary>
    public static async Task EnsureAsync(string directory, HttpClient http, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        foreach (var file in Files)
        {
            var path = Path.Combine(directory, file.Name);
            if (new FileInfo(path) is { Exists: true } info && info.Length == file.Size)
            {
                continue;
            }

            var partial = path + ".part";
            await using (var output = File.Create(partial))
            {
                using var response = await http.GetAsync(Url(file), HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await input.CopyToAsync(output, ct);
            }

            await using (var check = File.OpenRead(partial))
            {
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct));
                if (hash != file.Sha256)
                {
                    File.Delete(partial);
                    throw new InvalidDataException($"{file.Name}: SHA-256 {hash}, expected {file.Sha256}.");
                }
            }

            File.Move(partial, path, overwrite: true);
        }
    }
}
