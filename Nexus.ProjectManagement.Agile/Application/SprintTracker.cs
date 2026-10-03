using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Application;

/// <summary>The three things about a task that affect a sprint's history.</summary>
public readonly record struct TaskSprintState(int? SprintNumber, AgileTaskStatus Status, int? StoryPoints)
{
    public static TaskSprintState Of(AgileTask task) => new(task.SprintNumber, task.Status, task.StoryPoints);

    /// <summary>A task that does not exist (yet, or any more) is in no sprint.</summary>
    public static TaskSprintState None => new(null, AgileTaskStatus.ToDo, null);

    public int Points => Math.Max(StoryPoints ?? 0, 0);
    public bool IsDone => Status == AgileTaskStatus.Done;
}

/// <summary>
/// Turns changes to a task into the sprint events the burn charts and velocity are replayed from.
/// A task only knows its current sprint, status and points, so this is the only place that history
/// is written. It adds events to the repository and never saves; the caller saves, so a task and
/// its events are stored together or not at all.
///
/// A change is expressed as "undo what the task contributed before, then apply what it
/// contributes now": scope (its points, while it is in a sprint) and completion (the same points,
/// while it is also Done). Where the sprint is unchanged only the differences are recorded. A
/// sprint that is completed is frozen - its history no longer changes - and a sprint number
/// without a Sprint behind it records nothing.
/// </summary>
public sealed class SprintTracker(ISprintRepository sprints, ISprintEventRepository events, TimeProvider timeProvider)
{
    public async Task RecordChangeAsync(
        Guid tenantId, Guid projectId, Guid taskId, TaskSprintState before, TaskSprintState after, CancellationToken cancellationToken)
    {
        if (before == after)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var sameSprint = before.SprintNumber == after.SprintNumber;

        // What the task contributed before and contributes now, to each sprint that still counts it.
        var undo = await ActiveAsync(projectId, before.SprintNumber, cancellationToken);
        var apply = await ActiveAsync(projectId, after.SprintNumber, cancellationToken);

        async Task Add(int sprint, SprintEventType type, int points) =>
            await events.AddAsync(new SprintEvent(Guid.NewGuid(), tenantId, projectId, sprint, taskId, type, points, now), cancellationToken);

        if (sameSprint && undo)
        {
            var sprint = before.SprintNumber!.Value;

            // Within one sprint: only record what really differs.
            if (before.Points != after.Points)
            {
                await Add(sprint, SprintEventType.ScopeRemoved, before.Points);
                await Add(sprint, SprintEventType.ScopeAdded, after.Points);
            }

            switch (before.IsDone, after.IsDone)
            {
                case (true, true) when before.Points != after.Points:
                    await Add(sprint, SprintEventType.Reopened, before.Points);
                    await Add(sprint, SprintEventType.Completed, after.Points);
                    break;
                case (true, false):
                    await Add(sprint, SprintEventType.Reopened, before.Points);
                    break;
                case (false, true):
                    await Add(sprint, SprintEventType.Completed, after.Points);
                    break;
            }

            return;
        }

        if (undo)
        {
            var sprint = before.SprintNumber!.Value;
            if (before.IsDone)
            {
                await Add(sprint, SprintEventType.Reopened, before.Points);
            }

            await Add(sprint, SprintEventType.ScopeRemoved, before.Points);
        }

        if (apply)
        {
            var sprint = after.SprintNumber!.Value;
            await Add(sprint, SprintEventType.ScopeAdded, after.Points);
            if (after.IsDone)
            {
                await Add(sprint, SprintEventType.Completed, after.Points);
            }
        }
    }

    /// <summary>Records that a task was left unfinished when its sprint ended and moved on.</summary>
    public Task RecordCarriedOverAsync(Guid tenantId, Guid projectId, int sprintNumber, Guid taskId, int points, CancellationToken cancellationToken) =>
        events.AddAsync(new SprintEvent(
            Guid.NewGuid(), tenantId, projectId, sprintNumber, taskId, SprintEventType.CarriedOver, Math.Max(points, 0), timeProvider.GetUtcNow()),
            cancellationToken);

    /// <summary>True when the number is a sprint whose history is still being written.</summary>
    private async Task<bool> ActiveAsync(Guid projectId, int? sprintNumber, CancellationToken cancellationToken)
    {
        if (sprintNumber is not { } number)
        {
            return false;
        }

        var sprint = await sprints.GetByNumberAsync(projectId, number, cancellationToken);
        return sprint is not null && sprint.Status != SprintStatus.Completed;
    }
}
