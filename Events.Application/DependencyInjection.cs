using Events.Application.Commands.CreateEvent;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Events.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddEventsApplication(this IServiceCollection services)
    {
        services.AddMediatR(typeof(CreateEventCommand).Assembly);

        return services;
    }
}
