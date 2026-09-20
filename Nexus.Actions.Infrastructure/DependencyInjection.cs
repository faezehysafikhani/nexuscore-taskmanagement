using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Actions.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.Actions.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddActionManagementInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ActionsDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IActionsUnitOfWork>(provider => provider.GetRequiredService<ActionsDbContext>());
        services.AddScoped<IActionItemRepository, ActionItemRepository>();

        return services;
    }
}
