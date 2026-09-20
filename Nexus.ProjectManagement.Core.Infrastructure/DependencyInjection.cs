using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Core.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Core.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectManagementCoreInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ProjectManagementCoreDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IProjectManagementUnitOfWork>(provider => provider.GetRequiredService<ProjectManagementCoreDbContext>());
        services.AddScoped<IProjectRepository, ProjectRepository>();

        return services;
    }
}
