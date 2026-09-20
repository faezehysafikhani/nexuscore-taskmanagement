using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.StakeholderManagement.Application;
using Nexus.ProjectManagement.StakeholderManagement.Application.Dtos;
using Nexus.ProjectManagement.StakeholderManagement.Application.EventHandlers;
using Nexus.ProjectManagement.StakeholderManagement.Application.Validators;
using Nexus.ProjectManagement.StakeholderManagement.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore(). Optional: AddWorkflowApplication() and
    /// any IStakeholderAnalyzer (AI) - fully usable with neither installed.</summary>
    public static IServiceCollection AddStakeholderManagement(this IServiceCollection services)
    {
        services.AddScoped<IStakeholderService, StakeholderService>();
        services.AddScoped<IValidator<CreateStakeholderRequest>, CreateStakeholderRequestValidator>();
        services.AddScoped<IValidator<UpdateStakeholderRequest>, UpdateStakeholderRequestValidator>();
        services.AddSingleton<IPermissionCatalog, StakeholderPermissionCatalog>();

        services.AddScoped<IDomainEventHandler<ApprovalGranted>, StakeholderApprovalGrantedHandler>();
        services.AddScoped<IDomainEventHandler<ApprovalRejected>, StakeholderApprovalRejectedHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in StakeholderPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
