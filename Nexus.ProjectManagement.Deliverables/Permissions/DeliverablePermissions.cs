using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Deliverables.Permissions;

public static class DeliverablePermissions
{
    public const string View = "Deliverables.View";
    public const string Create = "Deliverables.Create";
    public const string Edit = "Deliverables.Edit";
    public const string Delete = "Deliverables.Delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Deliverables", "مشاهده تحویل‌دادنی‌ها"),
        new(Create, "Deliverables", "ایجاد تحویل‌دادنی"),
        new(Edit, "Deliverables", "ویرایش تحویل‌دادنی‌ها و وضعیت آن‌ها"),
        new(Delete, "Deliverables", "حذف تحویل‌دادنی‌ها")
    ];
}

public sealed class DeliverablePermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => DeliverablePermissions.All;
}
