using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Raci.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Raci.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectRaciInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RaciDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IRaciUnitOfWork>(provider => provider.GetRequiredService<RaciDbContext>());
        services.AddScoped<IRaciRepository, RaciRepository>();

        return services;
    }
}
