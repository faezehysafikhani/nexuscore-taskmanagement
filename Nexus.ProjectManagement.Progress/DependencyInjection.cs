using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Application.EventHandlers;
using Nexus.ProjectManagement.Progress.Application.Validators;
using Nexus.ProjectManagement.Progress.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Progress;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore(). Optional: AddWorkflowApplication() and
    /// any IExecutiveSummaryGenerator (AI) - fully usable with neither installed.</summary>
    public static IServiceCollection AddProgressManagement(this IServiceCollection services)
    {
        services.AddScoped<IProgressService, ProgressService>();
        services.AddScoped<IValidator<CreateProgressUpdateRequest>, CreateProgressUpdateRequestValidator>();
        services.AddScoped<IValidator<UpdateProgressUpdateRequest>, UpdateProgressUpdateRequestValidator>();
        services.AddSingleton<IPermissionCatalog, ProgressPermissionCatalog>();

        services.AddScoped<IDomainEventHandler<ApprovalGranted>, ProgressApprovalGrantedHandler>();
        services.AddScoped<IDomainEventHandler<ApprovalRejected>, ProgressApprovalRejectedHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ProgressPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
