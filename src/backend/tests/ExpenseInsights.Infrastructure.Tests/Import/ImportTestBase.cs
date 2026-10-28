using ExpenseInsights.Import.Ing;
using ExpenseInsights.Infrastructure.Import;
using ExpenseInsights.Infrastructure.Persistence;
using ExpenseInsights.Testing;

namespace ExpenseInsights.Infrastructure.Tests.Import;

/// <summary>Each test imports as its own fresh user, so tests sharing the database stay independent.</summary>
public abstract class ImportTestBase(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    public static readonly IngParserOptions ParserOptions = new()
    {
        PaymentProcessors = ["PayU", "Cashbill", "PayPro", "Przelewy24"],
    };

    protected ListLogger Logs { get; } = new();

    protected Task<Guid> NewUserAsync() => Postgres.CreateUserAsync(CancellationToken);

    protected async Task<ImportOutcome> ImportAsync(Guid userId, byte[] file, string name = "Lista_transakcji.csv")
    {
        await using var db = Postgres.CreateDbContext(userId, Logs);
        return await CreateImporter(db).ImportAsync(new MemoryStream(file), name, CancellationToken);
    }

    protected Task<ImportOutcome> ImportAsync(Guid userId, string testDataFile) =>
        ImportAsync(userId, TestData.Read(testDataFile), testDataFile);

    protected StatementImporter CreateImporter(AppDbContext db) =>
        new(db, [new IngCsvParser(ParserOptions)], TimeProvider.System, Logs.CreateLogger<StatementImporter>());

    protected static byte[] Encode(string text) => IngEncoding.GetBytes(text);

    protected static string Decode(byte[] bytes) => IngEncoding.GetString(bytes);

    private static readonly System.Text.Encoding IngEncoding = System.Text.CodePagesEncodingProvider.Instance.GetEncoding(1250)!;
}
