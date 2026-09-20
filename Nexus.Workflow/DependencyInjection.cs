using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Workflow.Application;
using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Application.Validators;
using Nexus.Workflow.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.Workflow;

public static class DependencyInjection
{
    public static IServiceCollection AddWorkflowApplication(this IServiceCollection services)
    {
        services.AddScoped<IWorkflowDefinitionService, WorkflowDefinitionService>();
        services.AddScoped<IWorkflowInstanceService, WorkflowInstanceService>();
        services.AddScoped<IValidator<CreateWorkflowDefinitionRequest>, CreateWorkflowDefinitionRequestValidator>();
        services.AddScoped<IValidator<AddWorkflowStepRequest>, AddWorkflowStepRequestValidator>();
        services.AddSingleton<IPermissionCatalog, WorkflowPermissionCatalog>();

        // Replace the default NullApprovalRequester - order-independent regardless of whether
        // NexusCore's AddInfrastructure() or this call runs first.
        services.Replace(ServiceDescriptor.Scoped<IApprovalRequester, WorkflowApprovalRequester>());

        services.AddAuthorization(options =>
        {
            foreach (var permission in WorkflowPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
