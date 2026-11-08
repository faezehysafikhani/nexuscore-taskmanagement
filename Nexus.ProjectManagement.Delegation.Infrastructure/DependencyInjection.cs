using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Delegation.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Delegation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddItemDelegationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ItemDelegationDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IItemDelegationUnitOfWork>(provider => provider.GetRequiredService<ItemDelegationDbContext>());
        services.AddScoped<IItemDelegationRepository, ItemDelegationRepository>();

        return services;
    }
}
