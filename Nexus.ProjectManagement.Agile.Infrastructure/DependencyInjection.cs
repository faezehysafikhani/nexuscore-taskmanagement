using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Agile.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Agile.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAgilePlanningInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AgileDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IAgileUnitOfWork>(provider => provider.GetRequiredService<AgileDbContext>());
        services.AddScoped<IAgileTaskRepository, AgileTaskRepository>();

        return services;
    }
}
