using NexusCore.Application.Identity.Permissions;

namespace Nexus.OrgStructure.Permissions;

public static class OrgStructurePermissions
{
    public const string View = "org_structure.view";
    public const string ManageCompanies = "org_structure.manage_companies";
    public const string ManagePositions = "org_structure.manage_positions";
    public const string ManageAssignments = "org_structure.manage_assignments";

    /// <summary>
    /// The builder of a work team may add people of other deputies too (without it, only their own
    /// deputy's). Not one of <see cref="All"/>: it gives no access to the structure itself.
    /// </summary>
    public const string TeamMembersFromAnyUnit = "org_structure.team_members_any_unit";

    /// <summary>
    /// Where tasks are limited to the user's own level and below (OrgStructure:LimitTaskAssignment),
    /// its holder may make anyone responsible. Not one of <see cref="All"/> either.
    /// </summary>
    public const string AssignTasksToAnyLevel = "org_structure.assign_tasks_any_level";

    /// <summary>
    /// Where tasks are given to posts: sees every vacant post's tasks (the substitution inbox) and
    /// puts another post in charge of them (سرپرست). Without it, only the posts below oneself.
    /// Not one of <see cref="All"/> either.
    /// </summary>
    public const string ManageSubstitution = "org_structure.manage_substitution";

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

    /// <summary>Permissions about work teams that only mean something with the organization structure.</summary>
    public static IReadOnlyList<PermissionDefinition> WorkTeams { get; } =
    [
        new(TeamMembersFromAnyUnit, "Organization", "افزودن اعضای سایر معاونت‌ها به تیم کاری"),
        new(AssignTasksToAnyLevel, "Organization", "ارجاع فعالیت به همه‌ی سطوح سازمانی (حتی بالاتر از خود)"),
        new(ManageSubstitution, "Organization", "مدیریت کارتابل جانشینی (تعیین سرپرست برای همه‌ی پست‌های خالی)")
    ];
}

public sealed class OrgStructurePermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => [.. OrgStructurePermissions.All, .. OrgStructurePermissions.WorkTeams];
}
