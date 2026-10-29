using Microsoft.Extensions.DependencyInjection;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.OrgStructure;

public static class DependencyInjection
{
    /// <summary>
    /// Companies, positions and the manager/subordinate hierarchy. Optional: a host that does not
    /// call this (and AddOrgStructureInfrastructure / MapOrgStructureEndpoints) has none of it -
    /// no tables, no endpoints, no permissions. Needs Nexus.Organization (its units are the
    /// deputies and departments) to be registered too.
    /// </summary>
    public static IServiceCollection AddOrgStructure(this IServiceCollection services)
    {
        // The one graph of units everything reads (parents, subtrees, domains).
        services.AddScoped<IOrgGraphProvider, OrgGraphProvider>();
        services.AddScoped<IOrgStructureService, OrgStructureService>();
        // The dynamic structure's nodes, types and rules, and the impact preview of a change.
        services.AddScoped<OrgNodeService>();
        services.AddScoped<IOrgNodeService>(sp => sp.GetRequiredService<OrgNodeService>());
        services.AddScoped<IOrgLinkService>(sp => sp.GetRequiredService<OrgNodeService>());
        services.AddScoped<IOrgTemplateService>(sp => sp.GetRequiredService<OrgNodeService>());
        services.AddScoped<IOrgImpactPreview, OrgImpactPreview>();
        services.AddScoped<IHonorificService, HonorificService>();
        services.AddSingleton<IPermissionCatalog, OrgStructurePermissionCatalog>();
        // Work-team builders add their own deputy's people (more with TeamMembersFromAnyUnit).
        // The Core registers its unlimited default with TryAdd, so this one wins whichever runs first.
        services.AddScoped<IWorkTeamMemberScope, DeputyWorkTeamMemberScope>();
        // Tasks only to one's own level and below - when OrgStructure:LimitTaskAssignment is on.
        services.AddOptions<OrgStructureOptions>().BindConfiguration(OrgStructureOptions.SectionName);
        services.AddScoped<IWorkAssignmentScope, HierarchyWorkAssignmentScope>();
        // Everyone kept to their own company - when OrgStructure:LimitToOwnCompany is on.
        services.AddScoped<CompanyReachScope>();
        services.AddScoped<IUserReachScope>(provider => provider.GetRequiredService<CompanyReachScope>());
        // Tasks given to posts - when OrgStructure:AssignTasksToPosts is on (wins over the Core's default like the scopes above).
        services.AddScoped<TaskPostDirectory>();
        services.AddScoped<ITaskPosts>(provider => provider.GetRequiredService<TaskPostDirectory>());
        // Who was on each task through its posts, and when (for reports).
        services.AddScoped<ITaskPostHistory, TaskPostHistory>();
        services.AddScoped<ISubstitutionService, SubstitutionService>();
        // A disabled account leaves its posts.
        services.AddScoped<IUserDeactivationListener, PostReleaseOnDeactivation>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in OrgStructurePermissions.All.Concat(OrgStructurePermissions.WorkTeams)
                         .Where(permission => !OrgStructurePermissions.NodePolicies.ContainsKey(permission.Name)))
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }

            // Node operations: their own permission or the older one that covered them.
            foreach (var (name, accepted) in OrgStructurePermissions.NodePolicies)
            {
                options.AddPolicy(name, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
                    accepted.Any(permission => context.User.HasClaim("permission", permission))));
            }

            options.AddPolicy(OrgStructurePermissions.ReadPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireAssertion(context =>
                    OrgStructurePermissions.All.Any(permission => context.User.HasClaim("permission", permission.Name))));

            options.AddPolicy(OrgStructurePermissions.TitlesReadPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireAssertion(context =>
                    OrgStructurePermissions.TitleReaders.Concat(OrgStructurePermissions.All.Select(p => p.Name))
                        .Any(permission => context.User.HasClaim("permission", permission))));

            options.AddPolicy(OrgStructurePermissions.TitlesAssignPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireAssertion(context =>
                    OrgStructurePermissions.TitleAssigners.Any(permission => context.User.HasClaim("permission", permission))));
        });

        return services;
    }
}
