using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.History.Application;

namespace Nexus.ProjectManagement.History.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectHistoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // No interceptors on purpose - see ProjectHistoryDbContext.
        services.AddDbContext<ProjectHistoryDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IProjectHistoryUnitOfWork>(provider => provider.GetRequiredService<ProjectHistoryDbContext>());
        services.AddScoped<IProjectHistoryRepository, ProjectHistoryRepository>();

        return services;
    }
}
