using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Contracts.Permissions;

public static class ContractPermissions
{
    public const string View = "Contracts.View";
    public const string Create = "Contracts.Create";
    public const string Edit = "Contracts.Edit";
    public const string Delete = "Contracts.Delete";
    public const string Submit = "Contracts.Submit";
    public const string ManageAddenda = "Contracts.ManageAddenda";
    public const string ManageInvoices = "Contracts.ManageInvoices";
    public const string RecordPayments = "Contracts.RecordPayments";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ContractManagement", "مشاهده قراردادها، الحاقیه‌ها و صورتحساب‌ها"),
        new(Create, "ContractManagement", "ثبت قرارداد"),
        new(Edit, "ContractManagement", "ویرایش قرارداد و تغییر وضعیت آن"),
        new(Delete, "ContractManagement", "حذف قرارداد"),
        new(Submit, "ContractManagement", "ارسال قرارداد، الحاقیه و صورتحساب برای تأیید"),
        new(ManageAddenda, "ContractManagement", "مدیریت الحاقیه‌های قرارداد"),
        new(ManageInvoices, "ContractManagement", "مدیریت صورتحساب‌های قرارداد"),
        new(RecordPayments, "ContractManagement", "ثبت پرداخت صورتحساب")
    ];
}

public sealed class ContractPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ContractPermissions.All;
}
