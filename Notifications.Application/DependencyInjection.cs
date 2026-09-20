using System.Reflection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Notifications.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationApplication(
        this IServiceCollection services)
    {
        services.AddMediatR(
            Assembly.GetExecutingAssembly());

        return services;
    }
}