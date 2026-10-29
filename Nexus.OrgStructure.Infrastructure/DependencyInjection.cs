using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.OrgStructure.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.OrgStructure.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The module's tables live in the DefaultConnection database under the org_structure
    /// schema. The host creates them at startup with
    /// ModuleSchemaInitializer.EnsureCreatedAsync(OrgStructureDbContext) - only hosts that use
    /// this module ever get them.
    /// </summary>
    public static IServiceCollection AddOrgStructureInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrgStructureDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IOrgStructureUnitOfWork>(provider => provider.GetRequiredService<OrgStructureDbContext>());
        services.AddScoped<IOrgStructureRepository, OrgStructureRepository>();
        // One structure change at a time per organization (on SQL Server across every server).
        services.AddScoped<IOrgStructureWriteLock, SqlOrgStructureWriteLock>();
        services.AddHostedService<TaskPostsStartup>();
        services.AddHostedService<AppointmentActivationService>();

        return services;
    }
}
