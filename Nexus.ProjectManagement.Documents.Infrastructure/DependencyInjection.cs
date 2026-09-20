using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Documents.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Documents.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectDocumentsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ProjectDocumentsDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IDocumentsUnitOfWork>(provider => provider.GetRequiredService<ProjectDocumentsDbContext>());
        services.AddScoped<IProjectDocumentRepository, ProjectDocumentRepository>();

        return services;
    }
}
