using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Contracts.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddContractManagementInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ContractsDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IContractsUnitOfWork>(provider => provider.GetRequiredService<ContractsDbContext>());
        services.AddScoped<IContractRepository, ContractRepository>();

        return services;
    }
}
