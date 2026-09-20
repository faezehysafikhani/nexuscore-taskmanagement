using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Kpi.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Kpi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectKpiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<KpiDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IKpiUnitOfWork>(provider => provider.GetRequiredService<KpiDbContext>());
        services.AddScoped<IKpiRepository, KpiRepository>();

        return services;
    }
}
