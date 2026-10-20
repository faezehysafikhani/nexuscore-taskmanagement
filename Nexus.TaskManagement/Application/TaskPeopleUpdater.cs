using Nexus.TaskManagement.Domain;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Where tasks are given to posts: when a post's holder changes, its tasks get the new holder in
/// place of the old one - as their creator, responsible or with access - and are told so like
/// any change of a task's people.
/// </summary>
public sealed class TaskPeopleUpdater(
    ITaskRepository repository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    ITaskNoticePublisher notices) : ITaskPeopleUpdater
{
    public async Task ApplyAsync(Guid tenantId, IReadOnlyList<TaskPersonChange> changes, CancellationToken cancellationToken)
    {
        var changed = new List<(TaskItem Task, IReadOnlyList<Guid> ResponsibleBefore, IReadOnlyList<Guid> PeopleBefore)>();
        foreach (var group in changes.GroupBy(change => change.TaskId))
        {
            var task = await repository.GetByIdAsync(tenantId, group.Key, cancellationToken);
            if (task is null)
            {
                continue;
            }

            var responsibleBefore = task.ResponsibleUserIds;
            var peopleBefore = TaskPeople.Of(task);
            var responsible = responsibleBefore.ToList();
            var accessList = TaskPeople.AccessListOf(task).ToList();
            foreach (var change in group)
            {
                switch (change.Role)
                {
                    case TaskPostRole.Owner:
                        task.SetOwnerByPost(change.ToUserId);
                        break;
                    case TaskPostRole.Acting:
                        task.SetActing(change.FromUserId, change.ToUserId, change.Note);
                        break;
                    case TaskPostRole.Responsible:
                        Replace(responsible, change.FromUserId, change.ToUserId);
                        break;
                    default:
                        Replace(accessList, change.FromUserId, change.ToUserId);
                        break;
                }
            }

            task.SetResponsibleUsers(responsible);
            task.AssignUsers(accessList.Where(id => !responsible.Contains(id)));
            task.RecordAssignmentAndDueChanges(responsibleBefore, task.DueDate, task.DueTime, currentUser.UserId);
            changed.Add((task, responsibleBefore, peopleBefore));
        }

        if (changed.Count == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var (task, responsibleBefore, peopleBefore) in changed)
        {
            foreach (var (kind, userIds) in TaskPeople.Changes(responsibleBefore, peopleBefore, task, currentUser.UserId))
            {
                await notices.PublishAsync(
                    new TaskNotice(task.TenantId, task.Id, task.Title, currentUser.UserId, kind, kind.ToString(), null, userIds),
                    cancellationToken);
            }
        }
    }

    public Task<IReadOnlyList<TaskPeopleSnapshot>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        repository.ListPeopleAsync(tenantId, cancellationToken);

    public Task<IReadOnlyList<TaskSummary>> GetSummariesAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        repository.GetSummariesAsync(tenantId, taskIds, cancellationToken);

    /// <summary>The new person in the old one's place (kept in order); without an old one, added; without a new one, removed.</summary>
    private static void Replace(List<Guid> people, Guid? from, Guid? to)
    {
        var index = from is { } old ? people.IndexOf(old) : -1;
        if (index >= 0)
        {
            if (to is { } replacement && !people.Contains(replacement))
            {
                people[index] = replacement;
            }
            else
            {
                people.RemoveAt(index);
            }
        }
        else if (to is { } added && !people.Contains(added))
        {
            people.Add(added);
        }
    }
}
