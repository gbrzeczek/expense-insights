using ExpenseInsights.Infrastructure.Classification;
using ExpenseInsights.Infrastructure.Import;
using ExpenseInsights.Infrastructure.Insights;
using ExpenseInsights.Infrastructure.Persistence;
using ExpenseInsights.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExpenseInsights.Infrastructure;

public static class DependencyInjection
{
    /// <param name="services">The services.</param>
    /// <param name="connectionString">The Postgres connection string.</param>
    /// <param name="classification">Which tiers run after the rules; rules and memory only if not given.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, ClassificationOptions? classification = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));
        services.AddScoped<StatementImporter>();
        services.AddScoped<UserSignIn>();
        services.AddScoped<AccountDeletion>();
        services.AddSingleton(_ => classification is null ? new ClassificationTiers(true, null, null) : ClassificationTiers.Create(classification));
        services.AddScoped<TransactionClassifier>();
        services.AddScoped<Corrections>();
        services.AddSingleton<ClassificationRequests>();
        services.AddScoped<SpendingData>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
