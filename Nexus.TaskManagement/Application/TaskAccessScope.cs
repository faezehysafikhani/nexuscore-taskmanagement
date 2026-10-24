using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Which tasks the signed-in user may reach - the resource-level half of authorization, next
/// to the permission policies on the endpoints.
///
/// A user sees a task they own, are responsible for, are on the access list of (the task's
/// team is only its context: its members see it when they are on that list), and tasks
/// without an owner. Holders of Tasks.ManageAll see every task of
/// their organization - or, where everyone is kept to their own company (see IUserReachScope),
/// every task one of that company's people owns, is responsible for or is on the access list of.
/// The rule is applied inside the database queries (a query filter on TaskManagementDbContext),
/// so subtasks, comments, files, tags, schedules and history of a task the user cannot see are
/// unreachable too, whatever id is sent.
///
/// Work without a signed-in user (the recurrence scheduler) is not limited.
/// </summary>
public interface ITaskAccessScope
{
    /// <summary>False for background work and for Tasks.ManageAll holders.</summary>
    bool IsRestricted { get; }

    Guid? UserId { get; }

    /// <summary>Groups and teams the user belongs to. Empty until <see cref="EnsureLoadedAsync"/> ran.</summary>
    IReadOnlyList<Guid> GroupIds { get; }

    /// <summary>
    /// For a Tasks.ManageAll holder kept to their own company: its people (see the class summary).
    /// Null when not limited, and until <see cref="EnsureLoadedAsync"/> ran.
    /// </summary>
    IReadOnlyList<Guid>? ReachUserIds => null;

    Task EnsureLoadedAsync(CancellationToken cancellationToken);

    /// <summary>Changing a task's details, assignment, schedule or deleting it: its owner, or Tasks.ManageAll.</summary>
    bool CanManage(TaskItem task);
}

public sealed class TaskAccessScope(
    ICurrentUserContext currentUser,
    IUserDirectory userDirectory,
    IUserReachScope? reachScope = null) : ITaskAccessScope
{
    private IReadOnlyList<Guid> _groupIds = [];
    private IReadOnlyList<Guid>? _reach;
    private bool _loaded;

    public bool IsRestricted => currentUser.UserId is not null && !currentUser.HasPermission(TaskManagementPermissions.ManageAll);

    public Guid? UserId => currentUser.UserId;

    public IReadOnlyList<Guid> GroupIds => _groupIds;

    public IReadOnlyList<Guid>? ReachUserIds => _reach;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (IsRestricted && currentUser.UserId is { } userId)
        {
            _groupIds = await userDirectory.GetGroupIdsOfUserAsync(userId, cancellationToken);
        }
        else if (reachScope is not null && currentUser.UserId is { } manager && currentUser.TenantId is { } tenantId
            && await reachScope.GetReachAsync(tenantId, manager, cancellationToken) is { } reach)
        {
            _reach = reach.UserIds.ToList();
        }
    }

    public bool CanManage(TaskItem task) => !IsRestricted || task.OwnerUserId == currentUser.UserId;
}
