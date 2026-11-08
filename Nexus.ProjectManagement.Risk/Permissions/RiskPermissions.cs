using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.RiskManagement.Permissions;

public static class RiskPermissions
{
    public const string View = "Risks.View";
    public const string Create = "Risks.Create";
    public const string Edit = "Risks.Edit";
    public const string Delete = "Risks.Delete";
    public const string Submit = "Risks.Submit";
    public const string SubmitResponse = "RiskResponse.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "RiskManagement", "مشاهده فهرست ریسک‌ها"),
        new(Create, "RiskManagement", "ایجاد ریسک"),
        new(Edit, "RiskManagement", "ویرایش ریسک‌ها"),
        new(Delete, "RiskManagement", "حذف ریسک‌ها"),
        new(Submit, "RiskManagement", "ارسال ریسک‌ها برای تأیید"),
        new(SubmitResponse, "RiskManagement", "ارسال گزارش وضعیت اقدام پاسخ ریسک برای صحه‌گذاری")
    ];
}

public sealed class RiskPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => RiskPermissions.All;
}
