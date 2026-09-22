namespace NexusCore.Application.Identity.Options;

/// <summary>Identity:SelfRegistration. Off unless a host switches it on.</summary>
public sealed class SelfRegistrationOptions
{
    public const string SectionName = "Identity:SelfRegistration";

    public bool Enabled { get; set; }

    /// <summary>Tenant new accounts join. Empty means the only tenant, when there is exactly one.</summary>
    public string? TenantSlug { get; set; }

    /// <summary>Role given to every new account. Empty means none (an admin assigns one later).</summary>
    public string? DefaultRoleName { get; set; }
}

/// <summary>
/// Identity:SeedRoles - extra roles a host wants in the default tenant. Each is created once,
/// with the listed permissions, when it does not exist yet; later edits by an administrator
/// are never overwritten.
/// </summary>
public sealed class IdentitySeedOptions
{
    public const string SectionName = "Identity";

    public List<SeedRoleOptions> SeedRoles { get; set; } = [];

    /// <summary>
    /// Identity:AdminUsername - the sign-in name of the built-in administrator account the seeder
    /// creates. Users sign in with a username or mobile number only, so that account needs one.
    /// </summary>
    public string AdminUsername { get; set; } = "admin";
}

public sealed class SeedRoleOptions
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = [];
}
