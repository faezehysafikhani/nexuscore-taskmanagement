using NexusCore.Application.Identity.Permissions;

namespace Nexus.Workflow.Permissions;

public static class WorkflowPermissions
{
    public const string View = "Workflow.View";
    public const string Configure = "Workflow.Configure";
    public const string Approve = "Workflow.Approve";
    public const string Reject = "Workflow.Reject";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Workflow", "مشاهده تعاریف گردش‌کار و کارتابل تأیید"),
        new(Configure, "Workflow", "ایجاد و ویرایش تعاریف و مراحل گردش‌کار"),
        new(Approve, "Workflow", "تأیید مرحله در انتظار گردش‌کار"),
        new(Reject, "Workflow", "رد مرحله در انتظار گردش‌کار")
    ];
}

public sealed class WorkflowPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => WorkflowPermissions.All;
}
