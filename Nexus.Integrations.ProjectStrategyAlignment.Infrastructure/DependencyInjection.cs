using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Integrations.StrategyAlignment.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.Integrations.StrategyAlignment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectStrategyAlignmentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<StrategyAlignmentDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IStrategyAlignmentUnitOfWork>(provider => provider.GetRequiredService<StrategyAlignmentDbContext>());
        services.AddScoped<IAlignmentRepository, AlignmentRepository>();

        return services;
    }
}
