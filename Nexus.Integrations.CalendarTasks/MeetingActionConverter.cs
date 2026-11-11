using Nexus.Calendar.Application.Meetings;
using Nexus.Calendar.Infrastructure.Meetings;
using Nexus.Calendar.Permissions;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.CalendarTasks;

public sealed record ConvertedMeetingActionDto(int Index, Guid TaskId, string Title);
public sealed record ConvertMeetingActionsResult(MeetingDto Meeting, IReadOnlyList<ConvertedMeetingActionDto> Converted, IReadOnlyList<string> Skipped);

/// <summary>
/// "Minutes to tasks": an action item of a meeting becomes a real task through <see cref="ITaskService"/> (so every task
/// rule applies), the task keeps the meeting as its source, and the action keeps the task. Neither module knows the
/// other; this is the only place that does.
/// </summary>
public sealed class MeetingActionConverter(
    MeetingService meetings,
    ITaskService tasks,
    ITaskRepository taskRepository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser)
{
    public const string SourceType = "MeetingAction";

    /// <summary>Converts the action at <paramref name="index"/>, or every action not yet converted when it is null.</summary>
    public async Task<Result<ConvertMeetingActionsResult>> ConvertAsync(Guid meetingId, int? index, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<ConvertMeetingActionsResult>(Error.Unauthorized());
        }

        if (!currentUser.HasPermission(MeetingPermissions.Edit) || !currentUser.HasPermission("Tasks.Create"))
        {
            return Result.Failure<ConvertMeetingActionsResult>(Error.Forbidden("Turning actions into tasks needs the right to edit meetings and to create tasks."));
        }

        var loaded = await meetings.GetAsync(meetingId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<ConvertMeetingActionsResult>(loaded.Error);
        }

        var meeting = loaded.Value;
        if (!meeting.CanEdit)
        {
            return Result.Failure<ConvertMeetingActionsResult>(Error.Forbidden("Only the one who made or runs the meeting can turn its actions into tasks."));
        }

        var actions = meeting.Details.Actions.ToList();
        if (index is { } one && (one < 0 || one >= actions.Count))
        {
            return Result.Failure<ConvertMeetingActionsResult>(Error.NotFound("Action not found."));
        }

        var targets = index is { } only ? [only] : Enumerable.Range(0, actions.Count).Where(i => actions[i].TaskId is null).ToList();
        if (index is not null && actions[index.Value].TaskId is not null)
        {
            return Result.Failure<ConvertMeetingActionsResult>(Error.Conflict("This action is already a task."));
        }

        var converted = new List<ConvertedMeetingActionDto>();
        var skipped = new List<string>();
        var taskIds = meeting.Details.TaskIds.ToList();
        foreach (var i in targets)
        {
            var action = actions[i];
            var created = await tasks.CreateAsync(
                new CreateTaskRequest(
                    action.Title.Trim(),
                    action.DueDate ?? meeting.Details.Date.AddDays(7),
                    TaskPriority.Medium,
                    $"From the meeting \"{meeting.Details.Title}\" on {meeting.Details.Date:yyyy-MM-dd}.",
                    AssignedUserId: action.AssigneeUserId ?? userId),
                cancellationToken);
            if (created.IsFailure)
            {
                // The people rules may refuse one assignee; the others still go.
                skipped.Add($"{action.Title}: {created.Error.Message}");
                continue;
            }

            var task = await taskRepository.GetForUpdateAsync(tenantId, created.Value.Id, cancellationToken);
            task?.SetSource(SourceType, meeting.Id);
            actions[i] = action with { TaskId = created.Value.Id };
            taskIds.Add(created.Value.Id);
            converted.Add(new ConvertedMeetingActionDto(i, created.Value.Id, action.Title));
        }

        if (converted.Count == 0)
        {
            return skipped.Count > 0
                ? Result.Failure<ConvertMeetingActionsResult>(Error.Validation(string.Join(" | ", skipped)))
                : Result.Failure<ConvertMeetingActionsResult>(Error.Validation("There is no action left to turn into a task."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var saved = await meetings.SaveAsync(
            meeting.Id, meeting.Details with { Actions = actions, TaskIds = taskIds.Distinct().ToList(), Version = meeting.Version }, cancellationToken);
        return saved.IsFailure
            ? Result.Failure<ConvertMeetingActionsResult>(Error.Conflict($"The tasks were made, but the meeting could not be updated: {saved.Error.Message}"))
            : Result.Success(new ConvertMeetingActionsResult(saved.Value, converted, skipped));
    }
}
