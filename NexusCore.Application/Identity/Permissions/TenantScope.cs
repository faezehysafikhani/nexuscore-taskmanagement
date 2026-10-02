using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Application.Identity.Permissions;

/// <summary>
/// Which organization (tenant) a caller may act on. The tenant comes from the signed token,
/// never from the request: a tenantId in a query string or body is only accepted when it is the
/// caller's own, or when the caller holds tenants.manage_all. Background work (no signed-in
/// user) is not limited.
/// </summary>
public static class TenantScope
{
    public static bool CanAccessTenant(this ICurrentUserContext currentUser, Guid? tenantId)
    {
        if (currentUser.UserId is null)
        {
            return true;
        }

        return (tenantId is not null && tenantId == currentUser.TenantId)
            || currentUser.HasPermission(IdentityPermissions.TenantsManageAll);
    }

    /// <summary>
    /// The tenant a list request may read: the one asked for when allowed, otherwise the
    /// caller's own. Null (every tenant) only for callers allowed to see all of them.
    /// </summary>
    public static Guid? ResolveTenant(this ICurrentUserContext currentUser, Guid? requested)
    {
        if (currentUser.UserId is null || currentUser.HasPermission(IdentityPermissions.TenantsManageAll))
        {
            return requested ?? currentUser.TenantId;
        }

        return currentUser.TenantId;
    }
}
