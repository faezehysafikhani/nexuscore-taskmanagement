using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Actions.Application;
using Nexus.Actions.Application.Dtos;
using Nexus.Actions.Application.EventHandlers;
using Nexus.Actions.Application.Validators;
using Nexus.Actions.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.Actions;

public static class DependencyInjection
{
    /// <summary>Requires AddOrganizationApplication() and AddCalendarApplication() (hard
    /// dependencies). ProjectManagement.Core and Workflow are both optional - this module has
    /// no reference to either.</summary>
    public static IServiceCollection AddActionManagement(this IServiceCollection services)
    {
        services.AddScoped<IActionItemService, ActionItemService>();
        services.AddScoped<IValidator<CreateActionItemRequest>, CreateActionItemRequestValidator>();
        services.AddScoped<IValidator<UpdateActionItemRequest>, UpdateActionItemRequestValidator>();
        services.AddSingleton<IPermissionCatalog, ActionPermissionCatalog>();

        services.AddScoped<IDomainEventHandler<ApprovalGranted>, ActionApprovalGrantedHandler>();
        services.AddScoped<IDomainEventHandler<ApprovalRejected>, ActionApprovalRejectedHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ActionPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
