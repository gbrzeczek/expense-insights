using ExpenseInsights.Infrastructure.Import;
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
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
