using ExpenseInsights.Api;
using ExpenseInsights.Infrastructure;
using ExpenseInsights.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(
    builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is not configured."));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    await app.MigrateDatabaseAsync();
}

app.MapStaticAssets();

app.MapHealthChecks("/health");

var api = app.MapGroup("/api");
api.MapFallback(() => Results.Problem(statusCode: StatusCodes.Status404NotFound));

// Client-side routes of the Angular app. The shell must always be revalidated so a new
// deployment (and its service worker manifest) is picked up.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

await app.RunAsync();
