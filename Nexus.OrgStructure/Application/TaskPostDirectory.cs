using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.Organization.Application;
using Nexus.OrgStructure.Domain;
using NexusCore.Application.Identity.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Tasks given to posts, with <see cref="OrgStructureOptions.AssignTasksToPosts"/> on: keeps which
/// posts each task is given to (<see cref="TaskPost"/>) and, whenever a post's holder changes,
/// has the task module put the new holder on its tasks in place of the old one.
/// </summary>
public sealed class TaskPostDirectory(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    IOrganizationUnitRepository units,
    IOptions<OrgStructureOptions> options,
    IServiceProvider services) : ITaskPosts
{
    public bool Enabled => options.Value.AssignTasksToPosts;

    public async Task<IReadOnlyDictionary<Guid, WorkPost>> GetPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> postIds, CancellationToken cancellationToken)
    {
        var wanted = postIds.ToHashSet();
        return (await LoadAsync(tenantId, cancellationToken)).Posts.Where(post => wanted.Contains(post.PostId)).ToDictionary(post => post.PostId);
    }

    /// <summary>
    /// Every post - or, where the signed-in user is kept to their own company
    /// (<see cref="CompanyReachScope"/>), that company's posts only.
    /// </summary>
    public async Task<IReadOnlyList<WorkPost>> ListPostsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        if (services.GetService<NexusCore.SharedKernel.Interfaces.ICurrentUserContext>()?.UserId is not { } me
            || services.GetService<CompanyReachScope>() is not { } reach
            || await reach.GetCompaniesAsync(tenantId, me, cancellationToken) is not { } companies)
        {
            return state.Posts;
        }

        var companyOf = state.Seats.ToDictionary(seat => seat.Id, seat => seat.CompanyId);
        return state.Posts.Where(post => companyOf.TryGetValue(post.PostId, out var company) && companies.Contains(company)).ToList();
    }

    /// <summary>A person's main post: the one of their main position in their own place, else the first they hold.</summary>
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetMainPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        var result = new Dictionary<Guid, Guid>();
        foreach (var userId in userIds.Distinct())
        {
            var held = state.Holdings.Where(h => h.UserId == userId).Select(h => h.UnitPositionId).ToHashSet();
            var seats = state.Seats.Where(seat => held.Contains(seat.Id) && state.Posts.Any(post => post.PostId == seat.Id)).ToList();
            if (seats.Count == 0)
            {
                continue;
            }

            var assignment = state.Assignments.FirstOrDefault(a => a.UserId == userId);
            var main = seats.FirstOrDefault(seat => assignment is not null && seat.CompanyId == assignment.CompanyId
                    && seat.UnitId == assignment.UnitId && seat.PositionId == assignment.PositionId)
                ?? seats.FirstOrDefault(seat => assignment is not null && seat.UnitId == assignment.UnitId)
                ?? seats.OrderBy(seat => seat.UnitId is not null).ThenBy(seat => seat.SortOrder).First();
            result[userId] = main.Id;
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, TaskPostSet>> GetTaskPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        var rows = await repository.ListTaskPostsAsync(tenantId, taskIds.ToList(), null, cancellationToken);
        return rows.GroupBy(row => row.TaskId).ToDictionary(
            group => group.Key,
            group => new TaskPostSet(
                group.FirstOrDefault(row => row.Role == (int)TaskPostRole.Owner)?.PostId,
                group.Where(row => row.Role == (int)TaskPostRole.Responsible).OrderBy(row => row.CreatedAtUtc).Select(row => row.PostId).ToList(),
                group.Where(row => row.Role == (int)TaskPostRole.Access).OrderBy(row => row.CreatedAtUtc).Select(row => row.PostId).ToList()));
    }

    public async Task SetTaskPostsAsync(
        Guid tenantId, Guid taskId, Guid? ownerPostId, IReadOnlyList<Guid>? responsiblePostIds, IReadOnlyList<Guid>? accessPostIds,
        CancellationToken cancellationToken)
    {
        var rows = await repository.ListTaskPostsAsync(tenantId, [taskId], null, cancellationToken);
        var holders = (await LoadAsync(tenantId, cancellationToken)).Posts.ToDictionary(post => post.PostId, post => post.HolderUserId);

        async Task SetRoleAsync(TaskPostRole role, IReadOnlyList<Guid> postIds)
        {
            var current = rows.Where(row => row.Role == (int)role).ToList();
            current.Where(row => !postIds.Contains(row.PostId)).ToList().ForEach(repository.RemoveTaskPost);
            foreach (var postId in postIds.Distinct().Where(id => current.All(row => row.PostId != id)))
            {
                await repository.AddTaskPostAsync(
                    new TaskPost(Guid.NewGuid(), tenantId, taskId, postId, (int)role, holders.GetValueOrDefault(postId)), cancellationToken);
            }
        }

        if (ownerPostId is { } owner && rows.All(row => row.Role != (int)TaskPostRole.Owner))
        {
            await SetRoleAsync(TaskPostRole.Owner, [owner]);
        }

        if (responsiblePostIds is not null)
        {
            await SetRoleAsync(TaskPostRole.Responsible, responsiblePostIds);
        }

        if (accessPostIds is not null)
        {
            await SetRoleAsync(TaskPostRole.Access, accessPostIds);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Brings tasks up to date with their posts' holders (only those of these posts; null: all):
    /// the new holder takes the old one's place on each task. Someone who still holds another
    /// post the task has in the same role keeps it.
    /// </summary>
    public async Task SyncHoldersAsync(Guid tenantId, IReadOnlyCollection<Guid>? postIds, CancellationToken cancellationToken)
    {
        if (!Enabled || postIds is { Count: 0 })
        {
            return;
        }

        await SyncTaskHoldersAsync(tenantId, postIds, cancellationToken);
        await SyncActingAsync(tenantId, cancellationToken);
    }

    private async Task SyncTaskHoldersAsync(Guid tenantId, IReadOnlyCollection<Guid>? postIds, CancellationToken cancellationToken)
    {
        var affected = await repository.ListTaskPostsAsync(tenantId, null, postIds?.ToList(), cancellationToken);
        if (affected.Count == 0)
        {
            return;
        }

        var holders = (await LoadAsync(tenantId, cancellationToken)).Posts.ToDictionary(post => post.PostId, post => post.HolderUserId);
        var stale = affected.Where(row => row.HolderUserId != holders.GetValueOrDefault(row.PostId)).ToList();
        if (stale.Count == 0)
        {
            return;
        }

        // The other posts of the same tasks: someone holding one of them in the same role stays on.
        var allOfTasks = await repository.ListTaskPostsAsync(tenantId, stale.Select(row => row.TaskId).Distinct().ToList(), null, cancellationToken);
        var changes = stale.Select(row =>
        {
            var from = row.HolderUserId;
            if (from is { } old && allOfTasks.Any(other => other.Id != row.Id && other.TaskId == row.TaskId && other.Role == row.Role
                    && holders.GetValueOrDefault(other.PostId) == old))
            {
                from = null;
            }

            return new TaskPersonChange(row.TaskId, (TaskPostRole)row.Role, from, holders.GetValueOrDefault(row.PostId));
        }).ToList();

        if (services.GetService<ITaskPeopleUpdater>() is { } updater)
        {
            await updater.ApplyAsync(tenantId, changes, cancellationToken);
        }

        stale.ForEach(row => row.HeldBy(holders.GetValueOrDefault(row.PostId)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Brings stand-ins (سرپرستی) up to date: the acting post's holder sees the vacant post's tasks
    /// as its stand-in (جانشین); a post that got a holder, or a vacant post that no longer exists,
    /// ends its stand-in and takes the view back.
    /// </summary>
    public async Task SyncActingAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return;
        }

        var actings = await repository.ListPostActingsAsync(tenantId, cancellationToken);
        if (actings.Count == 0)
        {
            return;
        }

        var posts = (await LoadAsync(tenantId, cancellationToken)).Posts.ToDictionary(post => post.PostId);
        var taskPosts = await repository.ListTaskPostsAsync(tenantId, null, actings.Select(a => a.VacantPostId).ToList(), cancellationToken);
        var changes = new List<TaskPersonChange>();
        var ended = new List<PostActing>();
        foreach (var acting in actings)
        {
            var stillVacant = posts.TryGetValue(acting.VacantPostId, out var vacant) && vacant.HolderUserId is null;
            var desired = stillVacant ? posts.GetValueOrDefault(acting.ActingPostId)?.HolderUserId : null;
            if (desired != acting.AppliedUserId)
            {
                var note = vacant is null ? "جانشین" : $"جانشین {vacant.Title} · کد {vacant.Code}";
                changes.AddRange(taskPosts.Where(row => row.PostId == acting.VacantPostId).Select(row => row.TaskId).Distinct()
                    .Select(taskId => new TaskPersonChange(taskId, TaskPostRole.Acting, acting.AppliedUserId, desired, note)));
                acting.Applied(desired);
            }

            if (!stillVacant)
            {
                ended.Add(acting);
            }
        }

        if (changes.Count > 0 && services.GetService<ITaskPeopleUpdater>() is { } updater)
        {
            await updater.ApplyAsync(tenantId, changes, cancellationToken);
        }

        ended.ForEach(repository.RemovePostActing);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Gives tasks that have no posts yet to their people's main posts (creator, responsible and
    /// access each). People with no post stay on them as people. Safe to run again.
    /// </summary>
    public async Task ConvertTasksAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!Enabled || services.GetService<ITaskPeopleUpdater>() is not { } updater)
        {
            return;
        }

        var tasks = await updater.ListAsync(tenantId, cancellationToken);
        var withPosts = (await repository.ListTaskPostsAsync(tenantId, null, null, cancellationToken)).Select(row => row.TaskId).ToHashSet();
        var pending = tasks.Where(task => !withPosts.Contains(task.TaskId)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var people = pending.SelectMany(task => task.ResponsibleUserIds.Concat(task.AccessUserIds).Append(task.OwnerUserId ?? Guid.Empty))
            .Where(id => id != Guid.Empty).Distinct().ToList();
        var main = await GetMainPostsAsync(tenantId, people, cancellationToken);
        var added = false;
        foreach (var task in pending)
        {
            var rows = new List<(Guid PostId, TaskPostRole Role, Guid UserId)>();
            if (task.OwnerUserId is { } owner && main.TryGetValue(owner, out var ownerPost))
            {
                rows.Add((ownerPost, TaskPostRole.Owner, owner));
            }

            rows.AddRange(task.ResponsibleUserIds.Where(main.ContainsKey).Select(id => (main[id], TaskPostRole.Responsible, id)));
            rows.AddRange(task.AccessUserIds.Where(id => main.ContainsKey(id) && !task.ResponsibleUserIds.Contains(id)).Select(id => (main[id], TaskPostRole.Access, id)));
            foreach (var row in rows.DistinctBy(row => (row.PostId, row.Role)))
            {
                await repository.AddTaskPostAsync(new TaskPost(Guid.NewGuid(), tenantId, task.TaskId, row.PostId, (int)row.Role, row.UserId), cancellationToken);
                added = true;
            }
        }

        if (added)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<State> LoadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var unitNames = (await units.ListAsync(tenantId, cancellationToken)).ToDictionary(unit => unit.Id, unit => unit.Name);
        var companies = (await repository.ListCompaniesAsync(tenantId, cancellationToken)).ToDictionary(company => company.Id);
        var positions = (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToDictionary(position => position.Id);
        var seats = await repository.ListUnitPositionsAsync(tenantId, cancellationToken);
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        var posts = seats
            .Where(seat => positions.ContainsKey(seat.PositionId) && companies.ContainsKey(seat.CompanyId)
                && (seat.UnitId is not { } unitId || unitNames.ContainsKey(unitId)))
            .Select(seat => new WorkPost(
                seat.Id,
                holdings.FirstOrDefault(h => h.UnitPositionId == seat.Id)?.UserId,
                positions[seat.PositionId].Title,
                seat.Code,
                seat.UnitId is { } unitId ? $"{unitNames[unitId]} · {companies[seat.CompanyId].Name}" : companies[seat.CompanyId].Name,
                positions[seat.PositionId].IsActive))
            .ToList();
        return new State(posts, seats, holdings, await repository.ListAssignmentsAsync(tenantId, cancellationToken));
    }

    private sealed record State(
        IReadOnlyList<WorkPost> Posts, IReadOnlyList<UnitPosition> Seats, IReadOnlyList<UserPosition> Holdings, IReadOnlyList<UserAssignment> Assignments);
}
