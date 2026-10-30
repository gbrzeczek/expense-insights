using System.Text.Json;
using System.Text.Json.Serialization;
using ExpenseInsights.Api;
using ExpenseInsights.Api.Auth;
using ExpenseInsights.Api.Classification;
using ExpenseInsights.Core.Import;
using ExpenseInsights.Import.Ing;
using ExpenseInsights.Infrastructure;
using ExpenseInsights.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.AddAppAuth();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is not configured."));
builder.Services.AddSingleton(builder.Configuration.GetSection(IngParserOptions.Section).Get<IngParserOptions>() ?? new());
builder.Services.AddSingleton<IStatementParser, IngCsvParser>();
builder.Services.AddHostedService<ClassificationWorker>();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    await app.MigrateDatabaseAsync();
}

await app.SeedOwnerInvitationAsync();

app.MapStaticAssets();

app.UseAppAuth();

app.MapHealthChecks("/health");

// Everything under /api needs a signed-in user unless an endpoint says otherwise.
var api = app.MapGroup("/api").RequireAuthorization();
api.MapAuthEndpoints(app.Environment);
api.MapMeEndpoints();
api.MapInvitationEndpoints();
api.MapImportEndpoints();
api.MapTransactionEndpoints();
api.MapCategoryEndpoints();
api.MapMerchantEndpoints();
api.MapFallback(() => Results.Problem(statusCode: StatusCodes.Status404NotFound));

// Client-side routes of the Angular app. The shell must always be revalidated so a new
// deployment (and its service worker manifest) is picked up.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

await app.RunAsync();
