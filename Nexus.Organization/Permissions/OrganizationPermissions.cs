using NexusCore.Application.Identity.Permissions;

namespace Nexus.Organization.Permissions;

public static class OrganizationPermissions
{
    public const string View = "organization_units.view";
    public const string Create = "organization_units.create";
    public const string Update = "organization_units.update";
    public const string Delete = "organization_units.delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Organization", "View organization units"),
        new(Create, "Organization", "Create organization units"),
        new(Update, "Organization", "Update organization units"),
        new(Delete, "Organization", "Deactivate organization units")
    ];
}

public sealed class OrganizationPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => OrganizationPermissions.All;
}
