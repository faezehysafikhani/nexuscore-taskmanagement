using NexusCore.Application.Identity.Permissions;

namespace Ticketing.Application.Common.Security;

public static class TicketingPermissions
{
    /// <summary>Your own tickets: the ones you raised or that are assigned to you.</summary>
    public const string View = "Tickets.View";
    public const string Create = "Tickets.Create";

    /// <summary>Every ticket of the organization: see, assign, prioritise and change status.</summary>
    public const string Manage = "Tickets.Manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Ticketing", "مشاهده تیکت‌های خود و ثبت نظر روی آن‌ها"),
        new(Create, "Ticketing", "ثبت تیکت"),
        new(Manage, "Ticketing", "مدیریت همه تیکت‌های سازمان")
    ];
}

public sealed class TicketingPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => TicketingPermissions.All;
}
