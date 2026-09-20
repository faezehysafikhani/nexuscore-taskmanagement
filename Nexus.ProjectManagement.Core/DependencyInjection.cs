using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Application.EventHandlers;
using Nexus.ProjectManagement.Core.Application.Validators;
using Nexus.ProjectManagement.Core.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Core;

public static class DependencyInjection
{
    /// <summary>
    /// The one method Applications call to get Project CRUD. Every other ProjectManagement
    /// capability (Waterfall, Risk, Team, ...) requires this to be registered first.
    /// </summary>
    public static IServiceCollection AddProjectManagementCore(this IServiceCollection services)
    {
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IValidator<CreateProjectRequest>, CreateProjectRequestValidator>();
        services.AddScoped<IValidator<UpdateProjectRequest>, UpdateProjectRequestValidator>();
        services.AddSingleton<IPermissionCatalog, ProjectPermissionCatalog>();

        services.AddScoped<IDomainEventHandler<ApprovalGranted>, ProjectApprovalGrantedHandler>();
        services.AddScoped<IDomainEventHandler<ApprovalRejected>, ProjectApprovalRejectedHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ProjectPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
