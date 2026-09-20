using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Progress.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Progress.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProgressManagementInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ProgressDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IProgressUnitOfWork>(provider => provider.GetRequiredService<ProgressDbContext>());
        services.AddScoped<IProgressRepository, ProgressRepository>();

        return services;
    }
}
