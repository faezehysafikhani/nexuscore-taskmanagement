using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Infrastructure.Security;

/// <summary>
/// Security:TenantFilter - the organization condition on every query (see <see cref="ITenantScope"/>).
/// The defaults are the intended behaviour; the switches are for an emergency.
/// </summary>
public sealed class TenantFilterOptions
{
    public const string SectionName = "Security:TenantFilter";

    /// <summary>False turns the filter off everywhere.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>DbContext names switched off alone, e.g. ["ChatDbContext"], while a problem with one of them is investigated.</summary>
    public string[] DisabledContexts { get; set; } = [];
}

/// <summary>
/// The tenant of the signed-in user of the current request. No filter without one (anonymous
/// requests, background work) and none for callers allowed to work across organizations.
/// </summary>
public sealed class HttpTenantScope(ICurrentUserContext currentUser, IOptions<TenantFilterOptions> options) : ITenantScope
{
    public Guid? FilterTenantIdFor(string contextName)
    {
        var configured = options.Value;
        if (!configured.Enabled || configured.DisabledContexts.Contains(contextName, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (currentUser.UserId is null || currentUser.TenantId is not { } tenantId)
        {
            return null;
        }

        return currentUser.HasPermission(IdentityPermissions.TenantsManageAll) ? null : tenantId;
    }
}
