using Microsoft.AspNetCore.Authorization;

namespace NexusCore.Application.Identity.Permissions;

/// <summary>
/// Lives in Application (not the Api host) so every module's own AddXxxApplication() can
/// register AddAuthorization policies for its own permissions without depending on
/// NexusCore.Api - the composition host is meant to depend on modules, never the other way.
/// </summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim("permission", requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
