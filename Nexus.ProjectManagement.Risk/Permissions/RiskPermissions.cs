using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.RiskManagement.Permissions;

public static class RiskPermissions
{
    public const string View = "Risks.View";
    public const string Create = "Risks.Create";
    public const string Edit = "Risks.Edit";
    public const string Delete = "Risks.Delete";
    public const string Submit = "Risks.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "RiskManagement", "مشاهده فهرست ریسک‌ها"),
        new(Create, "RiskManagement", "ایجاد ریسک"),
        new(Edit, "RiskManagement", "ویرایش ریسک‌ها"),
        new(Delete, "RiskManagement", "حذف ریسک‌ها"),
        new(Submit, "RiskManagement", "ارسال ریسک‌ها برای تأیید")
    ];
}

public sealed class RiskPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => RiskPermissions.All;
}
