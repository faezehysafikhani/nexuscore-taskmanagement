using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Meetings.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.ProjectManagement.Meetings.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectMeetingsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MeetingDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IMeetingUnitOfWork>(provider => provider.GetRequiredService<MeetingDbContext>());
        services.AddScoped<IMeetingRepository, MeetingRepository>();

        return services;
    }
}
