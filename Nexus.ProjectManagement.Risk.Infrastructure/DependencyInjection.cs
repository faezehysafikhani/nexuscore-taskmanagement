using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.RiskManagement.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.RiskManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRiskManagementInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RiskManagementDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IRiskUnitOfWork>(provider => provider.GetRequiredService<RiskManagementDbContext>());
        services.AddScoped<IRiskRepository, RiskRepository>();

        return services;
    }
}
