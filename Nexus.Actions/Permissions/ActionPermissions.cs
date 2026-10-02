using NexusCore.Application.Identity.Permissions;

namespace Nexus.Actions.Permissions;

public static class ActionPermissions
{
    public const string View = "Actions.View";
    public const string Create = "Actions.Create";
    public const string Edit = "Actions.Edit";
    public const string Delete = "Actions.Delete";
    public const string Submit = "Actions.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Actions", "مشاهده اقدامات"),
        new(Create, "Actions", "ایجاد اقدام"),
        new(Edit, "Actions", "ویرایش اقدامات"),
        new(Delete, "Actions", "لغو اقدامات"),
        new(Submit, "Actions", "ارسال اقدامات برای تأیید")
    ];
}

public sealed class ActionPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ActionPermissions.All;
}
