using NexusCore.Application.Identity.Permissions;

namespace Nexus.OrgStructure.Permissions;

public static class OrgStructurePermissions
{
    public const string View = "org_structure.view";
    public const string ManageCompanies = "org_structure.manage_companies";
    public const string ManagePositions = "org_structure.manage_positions";
    public const string ManageAssignments = "org_structure.manage_assignments";

    /// <summary>
    /// Policy for the read endpoints: any of the permissions above. A manager of companies,
    /// positions or assignments has to see them too, and this module cannot add entries to
    /// NexusCore's PermissionPrerequisites without changing NexusCore for every product.
    /// </summary>
    public const string ReadPolicy = "org_structure.read";

    /// <summary>
    /// Titles («آقای»، «خانم»...) go with user administration, not with this module's own
    /// permissions: whoever may add users manages the list (users.create), whoever may add or
    /// edit users gives them one (TitlesAssignPolicy), and anyone who may see users reads it.
    /// </summary>
    public const string TitlesReadPolicy = "org_structure.titles.read";
    public const string TitlesAssignPolicy = "org_structure.titles.assign";
    public const string TitlesManagePermission = IdentityPermissions.UsersCreate;

    public static readonly string[] TitleReaders =
        [IdentityPermissions.UsersView, IdentityPermissions.UsersCreate, IdentityPermissions.UsersUpdate];

    public static readonly string[] TitleAssigners = [IdentityPermissions.UsersCreate, IdentityPermissions.UsersUpdate];

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Organization", "مشاهده ساختار سازمانی (شرکت‌ها، سمت‌ها، مدیر و زیردستان کاربران)"),
        new(ManageCompanies, "Organization", "مدیریت شرکت‌ها و تعیین معاونت‌های هر شرکت"),
        new(ManagePositions, "Organization", "مدیریت سمت‌های سازمانی"),
        new(ManageAssignments, "Organization", "تعیین شرکت، معاونت، سمت و مدیر کاربران")
    ];
}

public sealed class OrgStructurePermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => OrgStructurePermissions.All;
}
