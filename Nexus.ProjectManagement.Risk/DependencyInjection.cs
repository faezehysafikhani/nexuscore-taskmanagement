using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using Nexus.ProjectManagement.RiskManagement.Application.EventHandlers;
using Nexus.ProjectManagement.RiskManagement.Application.Validators;
using Nexus.ProjectManagement.RiskManagement.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.RiskManagement;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore(). Optional: AddWorkflowApplication() and
    /// any IRiskAnalyzer (AI) - Risk Management is fully usable with neither installed.</summary>
    public static IServiceCollection AddRiskManagement(this IServiceCollection services)
    {
        services.AddScoped<IRiskService, RiskService>();
        services.AddScoped<IValidator<CreateRiskRequest>, CreateRiskRequestValidator>();
        services.AddScoped<IValidator<UpdateRiskRequest>, UpdateRiskRequestValidator>();
        services.AddSingleton<IPermissionCatalog, RiskPermissionCatalog>();

        services.AddScoped<IDomainEventHandler<ApprovalGranted>, RiskApprovalGrantedHandler>();
        services.AddScoped<IDomainEventHandler<ApprovalRejected>, RiskApprovalRejectedHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in RiskPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
