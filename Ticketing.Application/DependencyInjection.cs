using Ticketing.Application.Tickets.Commands.CreateTicket;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NexusCore.Application.Identity.Permissions;
using Ticketing.Application.Common.Security;

public static class DependencyInjection
{
    public static IServiceCollection AddTicketingApplication(this IServiceCollection services)
    {
        services.AddMediatR(typeof(CreateTicketCommandHandler).Assembly);

        services.AddSingleton<IPermissionCatalog, TicketingPermissionCatalog>();
        services.AddAuthorization(options =>
        {
            foreach (var permission in TicketingPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}