using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Deliverables.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Deliverables.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectDeliverablesInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DeliverablesDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IDeliverablesUnitOfWork>(provider => provider.GetRequiredService<DeliverablesDbContext>());
        services.AddScoped<IDeliverableRepository, DeliverableRepository>();

        return services;
    }
}
