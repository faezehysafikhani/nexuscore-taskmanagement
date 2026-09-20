namespace NexusCore.Infrastructure.Identity;

/// <summary>
/// Feature switch for the optional user-group permission axis.
/// Bound from configuration section "Features:UserGroups".
/// </summary>
public sealed class UserGroupOptions
{
    public const string SectionName = "Features:UserGroups";

    /// <summary>
    /// When false: the group endpoints are not mapped and group permissions are not
    /// resolved, so the system behaves exactly as if the feature did not exist.
    /// Defaults to false so a project that never configures it is unaffected.
    /// </summary>
    public bool Enabled { get; set; }
}
