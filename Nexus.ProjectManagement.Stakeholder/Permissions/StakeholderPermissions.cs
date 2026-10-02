using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.StakeholderManagement.Permissions;

public static class StakeholderPermissions
{
    public const string View = "Stakeholders.View";
    public const string Create = "Stakeholders.Create";
    public const string Edit = "Stakeholders.Edit";
    public const string Delete = "Stakeholders.Delete";
    public const string Submit = "Stakeholders.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "StakeholderManagement", "مشاهده فهرست ذی‌نفعان"),
        new(Create, "StakeholderManagement", "ایجاد ذی‌نفع"),
        new(Edit, "StakeholderManagement", "ویرایش ذی‌نفعان"),
        new(Delete, "StakeholderManagement", "حذف ذی‌نفعان"),
        new(Submit, "StakeholderManagement", "ارسال ذی‌نفعان برای تأیید")
    ];
}

public sealed class StakeholderPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => StakeholderPermissions.All;
}
