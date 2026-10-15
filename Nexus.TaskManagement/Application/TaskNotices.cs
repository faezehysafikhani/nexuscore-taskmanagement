using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Application;

/// <summary>What happened to a task, for the in-app notifications of the people it concerns.</summary>
public enum TaskNoticeKind
{
    /// <summary>An entry of the task's history (Action and Details as recorded there).</summary>
    Activity,
    Comment,
    Deleted,
    /// <summary>The recipients were made responsible for the task.</summary>
    Assigned,
    /// <summary>The recipients are no longer responsible for the task.</summary>
    Unassigned,
    /// <summary>The recipients were put on the task's access list.</summary>
    AccessGranted,
    /// <summary>The task is due soon; Details is its due moment, as the users read it.</summary>
    DueSoon,
    /// <summary>The task's due moment passed and it is not done; Details is that moment.</summary>
    Overdue,
    /// <summary>An occurrence of the recurring task is coming up; Details is when.</summary>
    RecurringSoon,
}

/// <param name="ActorUserId">Who did it (never among the recipients); null for the system.</param>
/// <param name="RecipientUserIds">Who to tell; the publisher keeps only active users of the task's organization.</param>
public sealed record TaskNotice(
    Guid TenantId,
    Guid TaskId,
    string TaskTitle,
    Guid? ActorUserId,
    TaskNoticeKind Kind,
    string Action,
    string? Details,
    IReadOnlyCollection<Guid> RecipientUserIds);

/// <summary>
/// Stores in-app notifications about what happens to tasks. The module's default does nothing;
/// Nexus.Integrations.TaskNotifications stores them as notifications. Must not throw: a
/// notification that could not be stored never fails the change it is about.
/// </summary>
public interface ITaskNoticePublisher
{
    Task PublishAsync(TaskNotice notice, CancellationToken cancellationToken);
}

public sealed class NullTaskNoticePublisher : ITaskNoticePublisher
{
    public Task PublishAsync(TaskNotice notice, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The people a task concerns: its creator, those responsible for it and those on its access list.</summary>
public static class TaskPeople
{
    public static IReadOnlyList<Guid> Of(TaskItem task)
    {
        var people = new List<Guid>();
        if (task.OwnerUserId is { } owner)
        {
            people.Add(owner);
        }

        people.AddRange(task.ResponsibleUserIds);
        people.AddRange(task.Assignees.Select(a => a.UserId));
        return people.Distinct().ToList();
    }

    /// <summary>Those on the access list without being responsible.</summary>
    public static IReadOnlyList<Guid> AccessListOf(TaskItem task) =>
        task.Assignees.Where(a => !task.IsResponsible(a.UserId)).Select(a => a.UserId).Distinct().ToList();

    /// <summary>
    /// Who to tell after the people of a task changed: newly responsible, no longer responsible,
    /// newly on the access list (not merely moved there from being responsible). The person who
    /// made the change is never told about it.
    /// </summary>
    public static IEnumerable<(TaskNoticeKind Kind, IReadOnlyList<Guid> UserIds)> Changes(
        IReadOnlyCollection<Guid> responsibleBefore, IReadOnlyCollection<Guid> peopleBefore, TaskItem after, Guid? actorUserId)
    {
        var responsibleNow = after.ResponsibleUserIds;
        var assigned = responsibleNow.Where(id => !responsibleBefore.Contains(id) && id != actorUserId).ToList();
        var unassigned = responsibleBefore.Where(id => !responsibleNow.Contains(id) && id != actorUserId).ToList();
        var granted = AccessListOf(after).Where(id => !peopleBefore.Contains(id) && id != actorUserId).ToList();
        return new[] { (TaskNoticeKind.Assigned, assigned), (TaskNoticeKind.Unassigned, unassigned), (TaskNoticeKind.AccessGranted, granted) }
            .Where(change => change.Item2.Count > 0)
            .Select(change => (change.Item1, (IReadOnlyList<Guid>)change.Item2));
    }
}
