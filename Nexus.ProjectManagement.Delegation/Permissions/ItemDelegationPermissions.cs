using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Delegation.Permissions;

public static class ItemDelegationPermissions
{
    public const string View = "ItemDelegation.View";
    public const string Delegate = "ItemDelegation.Delegate";
    public const string Manage = "ItemDelegation.Manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ItemDelegation", "مشاهده تفویض‌های کار"),
        new(Delegate, "ItemDelegation", "تفویض فعالیت، وظیفه یا اقدام به دیگری"),
        new(Manage, "ItemDelegation", "پایان‌دادن به تفویض‌های دیگران")
    ];
}

public sealed class ItemDelegationPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ItemDelegationPermissions.All;
}
