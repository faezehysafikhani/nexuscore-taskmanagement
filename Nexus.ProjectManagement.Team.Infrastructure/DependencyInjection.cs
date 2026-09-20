using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Team.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Team.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectTeamInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TeamDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<ITeamUnitOfWork>(provider => provider.GetRequiredService<TeamDbContext>());
        services.AddScoped<ITeamRepository, TeamRepository>();

        return services;
    }
}
