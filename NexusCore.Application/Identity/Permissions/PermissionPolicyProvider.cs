using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NexusCore.Application.Identity.Permissions;

/// <summary>
/// Safety net behind the per-module policy registrations.
///
/// Every permission is still registered as a named policy by the code that owns it (the host
/// for IdentityPermissions, each module's AddXxxApplication, AddUserGroupFeature for groups).
/// When an endpoint asks for a policy that nobody registered, ASP.NET Core's default provider
/// returns null and the request fails with an InvalidOperationException - a 500 that hides the
/// real problem and tells the caller nothing.
///
/// This provider answers that case with the standard permission policy for the requested
/// name: an authenticated user holding a "permission" claim of exactly that name. A user
/// without the claim gets 403, as for any other permission, and the missing registration is
/// logged once so it can still be fixed at its source. Access is never widened: the fallback
/// policy is exactly as strict as a registered one.
/// </summary>
public sealed class PermissionPolicyProvider(
    IOptions<AuthorizationOptions> options,
    IEnumerable<IPermissionCatalog> catalogs,
    ILogger<PermissionPolicyProvider> logger) : DefaultAuthorizationPolicyProvider(options)
{
    private readonly HashSet<string> _knownPermissions =
        catalogs.SelectMany(catalog => catalog.GetPermissions()).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _fallbackPolicies = new(StringComparer.Ordinal);

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var registered = await base.GetPolicyAsync(policyName);
        if (registered is not null)
        {
            return registered;
        }

        return _fallbackPolicies.GetOrAdd(policyName, name =>
        {
            if (_knownPermissions.Contains(name))
            {
                logger.LogWarning(
                    "Authorization policy '{Policy}' is a known permission but was never registered; using the standard permission policy.",
                    name);
            }
            else
            {
                logger.LogError(
                    "Authorization policy '{Policy}' is not registered and is not a known permission. Requests will be denied (403) until it is defined.",
                    name);
            }

            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(name))
                .Build();
        });
    }
}
