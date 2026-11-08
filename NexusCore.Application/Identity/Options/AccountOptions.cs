namespace NexusCore.Application.Identity.Options;

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
    /// Identity:DefaultRoleName - the role every new user starts with. Empty: the built-in «کاربر عادی». A product whose
    /// users need more than that to work at all (project management: a user without a project role sees nothing) names
    /// its own role here; if the organization has no role of that name the built-in one is used.
    /// </summary>
    public string? DefaultRoleName { get; set; }

    /// <summary>
    /// Identity:AdminUsername - the sign-in name of the built-in administrator account the seeder
    /// creates. Users sign in with a username or mobile number only, so that account needs one.
    /// </summary>
    public string AdminUsername { get; set; } = "admin";

    /// <summary>Required for the first administrator account in production.</summary>
    public string? AdminPassword { get; set; }
}

public sealed class SeedRoleOptions
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = [];
}
