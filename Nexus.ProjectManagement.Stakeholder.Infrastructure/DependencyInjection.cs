using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.StakeholderManagement.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.StakeholderManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddStakeholderManagementInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<StakeholderManagementDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IStakeholderUnitOfWork>(provider => provider.GetRequiredService<StakeholderManagementDbContext>());
        services.AddScoped<IStakeholderRepository, StakeholderRepository>();

        return services;
    }
}
