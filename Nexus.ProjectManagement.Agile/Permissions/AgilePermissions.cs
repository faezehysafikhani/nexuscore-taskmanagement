using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Agile.Permissions;

public static class AgilePermissions
{
    public const string View = "AgileTasks.View";
    public const string Create = "AgileTasks.Create";
    public const string Edit = "AgileTasks.Edit";
    public const string Delete = "AgileTasks.Delete";
    public const string Submit = "AgileTasks.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "AgilePlanning", "مشاهده بورد وظایف چابک"),
        new(Create, "AgilePlanning", "ایجاد وظیفه چابک"),
        new(Edit, "AgilePlanning", "ویرایش وظایف چابک"),
        new(Delete, "AgilePlanning", "حذف وظایف چابک"),
        new(Submit, "AgilePlanning", "ارسال وظایف چابک برای تأیید")
    ];
}

public sealed class AgilePermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => AgilePermissions.All;
}
