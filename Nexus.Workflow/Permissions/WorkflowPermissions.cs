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
        new(View, "Workflow", "View workflow definitions and the approval center"),
        new(Configure, "Workflow", "Create and edit workflow definitions and steps"),
        new(Approve, "Workflow", "Approve a pending workflow step"),
        new(Reject, "Workflow", "Reject a pending workflow step")
    ];
}

public sealed class WorkflowPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => WorkflowPermissions.All;
}
