using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Integrations.StrategyAlignment.Application;
using Nexus.Integrations.StrategyAlignment.Application.Dtos;
using Nexus.Integrations.StrategyAlignment.Application.Validators;
using Nexus.Integrations.StrategyAlignment.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.Integrations.StrategyAlignment;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore() and AddStrategyManagement() - neither of
    /// those requires this in return.</summary>
    public static IServiceCollection AddProjectStrategyAlignment(this IServiceCollection services)
    {
        services.AddScoped<IAlignmentService, AlignmentService>();
        services.AddScoped<IValidator<CreateAlignmentRequest>, CreateAlignmentRequestValidator>();
        services.AddScoped<IValidator<UpdateAlignmentRequest>, UpdateAlignmentRequestValidator>();
        services.AddSingleton<IPermissionCatalog, AlignmentPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in AlignmentPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
