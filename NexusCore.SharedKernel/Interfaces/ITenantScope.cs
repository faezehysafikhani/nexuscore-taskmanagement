namespace NexusCore.SharedKernel.Interfaces;

/// <summary>
/// The organization a request is confined to, read by the DbContexts to put a tenant condition
/// into every query of their organization-owned tables (global query filters). It is the safety
/// net under the conditions written by hand in the repositories: a query that forgets
/// <c>TenantId ==</c> still cannot return another organization's rows.
///
/// <see cref="FilterTenantIdFor"/> is null - no tenant condition - when there is nothing to
/// confine to: sign-in and other anonymous requests, background services and seeding (which work
/// across organizations on purpose and name the tenant themselves), callers who hold
/// tenants.manage_all, and a context that was switched off in the configuration
/// (Security:TenantFilter).
/// </summary>
public interface ITenantScope
{
    /// <param name="contextName">The DbContext asking, e.g. "ChatDbContext" - so one context can be switched off alone.</param>
    Guid? FilterTenantIdFor(string contextName);
}
