using Events.Application.Abstractions;
using Events.Infrastructure.Persistence;
using Events.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Events.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEventsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=localhost;Database=NexusCoreDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;";

        services.AddDbContext<EventsDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IEventsDbContext>(sp => sp.GetRequiredService<EventsDbContext>());

        services.AddHostedService<EventReminderBackgroundService>();

        return services;
    }
}
