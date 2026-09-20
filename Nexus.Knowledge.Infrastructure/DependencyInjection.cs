using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Knowledge.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.Knowledge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKnowledgeManagementInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<KnowledgeDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IKnowledgeUnitOfWork>(provider => provider.GetRequiredService<KnowledgeDbContext>());
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();

        return services;
    }
}
