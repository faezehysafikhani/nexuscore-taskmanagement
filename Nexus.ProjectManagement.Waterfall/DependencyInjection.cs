using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Application.EventHandlers;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Application.Validators;
using Nexus.ProjectManagement.Waterfall.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore() to already be registered. Optional:
    /// AddWorkflowApplication() (works standalone without it) and any IWbsGenerator.</summary>
    public static IServiceCollection AddWaterfallPlanning(this IServiceCollection services)
    {
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IActivityDependencyService, ActivityDependencyService>();
        services.AddScoped<IScheduleService, ScheduleService>();

        // Defaults that an installed integration replaces: no calendars known (every day is a
        // working day), and the system clock.
        services.TryAddScoped<IWorkingDayCalendarProvider, NullWorkingDayCalendarProvider>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IValidator<CreateActivityRequest>, CreateActivityRequestValidator>();
        services.AddScoped<IValidator<UpdateActivityRequest>, UpdateActivityRequestValidator>();
        services.AddScoped<IValidator<UpdateActivityProgressRequest>, UpdateActivityProgressRequestValidator>();
        services.AddScoped<IValidator<CreateActivityDependencyRequest>, CreateActivityDependencyRequestValidator>();
        services.AddScoped<IValidator<UpdateActivityDependencyRequest>, UpdateActivityDependencyRequestValidator>();
        services.AddSingleton<IPermissionCatalog, WaterfallPermissionCatalog>();

        services.AddScoped<IDomainEventHandler<ApprovalGranted>, ActivityApprovalGrantedHandler>();
        services.AddScoped<IDomainEventHandler<ApprovalRejected>, ActivityApprovalRejectedHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in WaterfallPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
