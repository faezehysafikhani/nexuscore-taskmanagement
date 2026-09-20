using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusCore.SharedKernel.Interfaces;
using Notifications.Application.Abstractions;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Services;

namespace Notifications.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<NotificationDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<INotificationDbContext>(
            provider =>
                provider.GetRequiredService<NotificationDbContext>());

        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }
}