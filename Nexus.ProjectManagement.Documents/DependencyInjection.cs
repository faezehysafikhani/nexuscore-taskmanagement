using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Documents.Application;
using Nexus.ProjectManagement.Documents.Application.Dtos;
using Nexus.ProjectManagement.Documents.Application.EventHandlers;
using Nexus.ProjectManagement.Documents.Application.Validators;
using Nexus.ProjectManagement.Documents.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Documents;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore(). File storage comes from NexusCore's
    /// always-present IFileStorage - no separate registration needed. Optional:
    /// AddWorkflowApplication(), IDocumentSummaryGenerator, IDocumentRelevanceAnalyzer (AI).</summary>
    public static IServiceCollection AddProjectDocuments(this IServiceCollection services)
    {
        services.AddScoped<IProjectDocumentService, ProjectDocumentService>();
        services.AddScoped<IValidator<UploadProjectDocumentRequest>, UploadProjectDocumentRequestValidator>();
        services.AddScoped<IValidator<UpdateProjectDocumentRequest>, UpdateProjectDocumentRequestValidator>();
        services.AddSingleton<IPermissionCatalog, ProjectDocumentPermissionCatalog>();

        services.AddScoped<IDomainEventHandler<ApprovalGranted>, ProjectDocumentApprovalGrantedHandler>();
        services.AddScoped<IDomainEventHandler<ApprovalRejected>, ProjectDocumentApprovalRejectedHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ProjectDocumentPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
