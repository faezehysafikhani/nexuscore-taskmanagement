namespace NexusCore.Application.Identity;

/// <summary>
/// The built-in «کاربر عادی» role: created once in the default organization (an administrator may
/// change it afterwards; it is never overwritten) and given to every new user, so a new user can
/// work at once. Permissions of modules a host does not have are left out.
/// </summary>
public static class DefaultUserRole
{
    public const string Name = "کاربر عادی";

    public const string Description = "نقش پیش‌فرض هر کاربر تازه: دیدن و ساختن فعالیت، ثبت دیدگاه و دیدن ساختار سازمانی.";

    public static readonly IReadOnlyList<string> Permissions =
    [
        "Tasks.View",
        "Tasks.Create",
        "Tasks.Comment",
        "org_structure.view",
        "org_structure.node.view",
    ];
}
