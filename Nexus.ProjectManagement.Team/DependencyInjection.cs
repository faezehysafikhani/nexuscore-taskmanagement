using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Team.Application;
using Nexus.ProjectManagement.Team.Application.Dtos;
using Nexus.ProjectManagement.Team.Application.Validators;
using Nexus.ProjectManagement.Team.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Team;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore(). Users (NexusCore Identity) is always
    /// present, so ListAvailableUsersAsync needs no extra registration.</summary>
    public static IServiceCollection AddProjectTeam(this IServiceCollection services)
    {
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<IValidator<CreateGovernanceRoleRequest>, CreateGovernanceRoleRequestValidator>();
        services.AddScoped<IValidator<UpdateGovernanceRoleRequest>, UpdateGovernanceRoleRequestValidator>();
        services.AddSingleton<IPermissionCatalog, TeamPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in TeamPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
