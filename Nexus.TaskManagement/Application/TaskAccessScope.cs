using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Which tasks the signed-in user may reach - the resource-level half of authorization, next
/// to the permission policies on the endpoints.
///
/// A user sees a task they own, are assigned to (directly, as a collaborator or through a team
/// they belong to), and tasks without an owner. Holders of Tasks.ManageAll see every task of
/// their organization. The rule is applied inside the database queries (a query filter on
/// TaskManagementDbContext), so subtasks, comments, files, tags, schedules and history of a task
/// the user cannot see are unreachable too, whatever id is sent.
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

    Task EnsureLoadedAsync(CancellationToken cancellationToken);

    /// <summary>Changing a task's details, assignment, schedule or deleting it: its owner, or Tasks.ManageAll.</summary>
    bool CanManage(TaskItem task);
}

public sealed class TaskAccessScope(ICurrentUserContext currentUser, IUserDirectory userDirectory) : ITaskAccessScope
{
    private IReadOnlyList<Guid> _groupIds = [];
    private bool _loaded;

    public bool IsRestricted => currentUser.UserId is not null && !currentUser.HasPermission(TaskManagementPermissions.ManageAll);

    public Guid? UserId => currentUser.UserId;

    public IReadOnlyList<Guid> GroupIds => _groupIds;

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
    }

    public bool CanManage(TaskItem task) => !IsRestricted || task.OwnerUserId == currentUser.UserId;
}
