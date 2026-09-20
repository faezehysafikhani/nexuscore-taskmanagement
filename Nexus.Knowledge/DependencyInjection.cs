using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Knowledge.Application;
using Nexus.Knowledge.Application.Dtos;
using Nexus.Knowledge.Application.Validators;
using Nexus.Knowledge.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.Knowledge;

public static class DependencyInjection
{
    /// <summary>No required dependency beyond NexusCore itself - Knowledge Management needs
    /// no Project. File storage comes from NexusCore's always-present IFileStorage.</summary>
    public static IServiceCollection AddKnowledgeManagement(this IServiceCollection services)
    {
        services.AddScoped<IKnowledgeDocumentService, KnowledgeDocumentService>();
        services.AddScoped<IValidator<UploadKnowledgeDocumentRequest>, UploadKnowledgeDocumentRequestValidator>();
        services.AddScoped<IValidator<UpdateKnowledgeDocumentRequest>, UpdateKnowledgeDocumentRequestValidator>();
        services.AddSingleton<IPermissionCatalog, KnowledgePermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in KnowledgePermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
