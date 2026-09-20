using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.StrategyManagement.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.StrategyManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddStrategyManagementInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<StrategyManagementDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IStrategyUnitOfWork>(provider => provider.GetRequiredService<StrategyManagementDbContext>());
        services.AddScoped<IStrategyRepository, StrategyRepository>();

        return services;
    }
}
