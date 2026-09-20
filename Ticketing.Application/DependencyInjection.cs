using Ticketing.Application.Tickets.Commands.CreateTicket;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddTicketingApplication(this IServiceCollection services)
    {
        services.AddMediatR(typeof(CreateTicketCommandHandler).Assembly);

        return services;
    }
}