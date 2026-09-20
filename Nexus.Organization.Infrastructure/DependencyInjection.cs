using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Organization.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.Organization.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrganizationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrganizationDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IOrganizationUnitOfWork>(provider => provider.GetRequiredService<OrganizationDbContext>());
        services.AddScoped<IOrganizationUnitRepository, OrganizationUnitRepository>();

        return services;
    }
}
