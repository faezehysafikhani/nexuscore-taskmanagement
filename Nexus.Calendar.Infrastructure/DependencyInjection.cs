using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Calendar.Application;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.Calendar.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCalendarInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CalendarDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<ICalendarUnitOfWork>(provider => provider.GetRequiredService<CalendarDbContext>());
        services.AddScoped<IWorkCalendarRepository, WorkCalendarRepository>();

        return services;
    }
}
