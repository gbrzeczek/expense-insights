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
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<StatementImporter>();
        services.AddScoped<UserSignIn>();
        services.AddScoped<AccountDeletion>();
        services.AddScoped<TransactionClassifier>();
        services.AddScoped<Corrections>();
        services.AddSingleton<ClassificationRequests>();
        services.AddScoped<SpendingData>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
