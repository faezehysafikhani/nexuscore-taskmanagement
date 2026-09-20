using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Waterfall.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Waterfall.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWaterfallPlanningInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<WaterfallDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IWaterfallUnitOfWork>(provider => provider.GetRequiredService<WaterfallDbContext>());
        services.AddScoped<IActivityRepository, ActivityRepository>();

        return services;
    }
}
