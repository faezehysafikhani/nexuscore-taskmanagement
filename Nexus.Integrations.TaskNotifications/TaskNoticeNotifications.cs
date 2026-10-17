using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.TaskManagement.Application;
using NexusCore.Application.Identity.Interfaces;
using Notifications.Application.Abstractions;

namespace Nexus.Integrations.TaskNotifications;

/// <summary>
/// What happens to a task, as in-app notifications for the people it concerns (its creator,
/// those responsible, those on its access list - never whoever did it, and only active users of
/// the task's organization). Stored on the server, so each person's list and read state are the
/// same on every device. Each notification carries its category, the task and who did it.
/// </summary>
public sealed class TaskNoticeNotifications(
    IServiceScopeFactory scopeFactory,
    ILogger<TaskNoticeNotifications> logger) : ITaskNoticePublisher
{
    public const string Created = "task.created";
    public const string Edited = "task.edit";
    public const string Activity = "task.activity";
    public const string Comment = "task.comment";
    public const string Deleted = "task.deleted";
    public const string Assigned = "task.assigned";
    public const string Unassigned = "task.unassigned";
    public const string AccessGranted = "task.access_granted";
    public const string Reminder = "task.reminder";
    public const string DueSoon = "task.due_soon";
    public const string Overdue = "task.overdue";
    public const string RecurringSoon = "task.recurring_soon";

    /// <summary>The history entry the task screen records itself for a new task (with its people).</summary>
    public const string AppCreationEntry = "ثبت و ایجاد فعالیت جدید";

    /// <summary>The server's own short entries for parts of an edit.</summary>
    private static readonly HashSet<string> ServerEditActions =
        ["Task updated", "Status changed", "Priority changed", "Assignee changed", "Team changed"];

    private static readonly HashSet<string> ServerActions =
    [
        "Task created", "Task updated", "Task deleted", "Status changed", "Priority changed", "Assignee changed",
        "Team changed", "Subtask added", "Subtask updated", "Subtask deleted", "Subtask completed", "Subtask reopened",
    ];

    /// <summary>How far apart the server's short entries of an edit and the task screen's full one can be.</summary>
    private static readonly TimeSpan SameEditWindow = TimeSpan.FromMinutes(2);

    public async Task PublishAsync(TaskNotice notice, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
            var ids = notice.RecipientUserIds.Distinct().ToList();
            if (notice.ActorUserId is { } actor)
            {
                ids.Add(actor);
            }

            var users = await directory.GetUsersAsync(ids, cancellationToken);
            var actorName = users.FirstOrDefault(u => u.Id == notice.ActorUserId)?.DisplayName;
            var recipients = users
                .Where(u => u.TenantId == notice.TenantId && u.IsActive && u.Id != notice.ActorUserId && notice.RecipientUserIds.Contains(u.Id))
                .Select(u => u.Id)
                .Distinct()
                .ToList();

            var (category, title, message) = Describe(notice);

            // The task screen describes an edit (or a new task) fully right after the server's
            // short entries: those are taken back, so each change is told once, clearly.
            if (notice.Kind == TaskNoticeKind.Activity && notice.ActorUserId is { } editor && !ServerActions.Contains(notice.Action))
            {
                var replaced = notice.Action == AppCreationEntry ? new[] { Created } : new[] { Edited };
                await scope.ServiceProvider.GetRequiredService<INotificationService>().RetractAsync(
                    new NotificationRetraction(notice.TenantId, replaced, notice.TaskId, editor, DateTime.UtcNow - SameEditWindow),
                    cancellationToken);
            }

            foreach (var userId in recipients)
            {
                await StoreAsync(new NotificationRequest(
                    userId, title, message, "Info", notice.TenantId, category, notice.TaskId, notice.ActorUserId, actorName), cancellationToken);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Notifications about task {TaskId} ({Kind}) were not stored.", notice.TaskId, notice.Kind);
        }
    }

    /// <summary>
    /// Category, title and text. A history entry keeps the history's own action and details, so
    /// the client shows it exactly as the task's history does.
    /// </summary>
    public static (string Category, string Title, string Message) Describe(TaskNotice notice) => notice.Kind switch
    {
        TaskNoticeKind.Activity when notice.Action == "Task created" => (Created, notice.Action, notice.Details ?? string.Empty),
        TaskNoticeKind.Activity when ServerEditActions.Contains(notice.Action) => (Edited, notice.Action, notice.Details ?? string.Empty),
        TaskNoticeKind.Activity => (Activity, notice.Action, notice.Details ?? string.Empty),
        TaskNoticeKind.Comment => (Comment, notice.TaskTitle, Shorten(notice.Details ?? string.Empty, 1000)),
        TaskNoticeKind.Deleted => (Deleted, notice.TaskTitle, $"فعالیت «{notice.TaskTitle}» حذف شد."),
        TaskNoticeKind.Assigned => (Assigned, notice.TaskTitle, $"مسئول انجام فعالیت «{notice.TaskTitle}» شدید."),
        TaskNoticeKind.Unassigned => (Unassigned, notice.TaskTitle, $"دیگر مسئول انجام فعالیت «{notice.TaskTitle}» نیستید."),
        TaskNoticeKind.AccessGranted => (AccessGranted, notice.TaskTitle, $"به فعالیت «{notice.TaskTitle}» دسترسی پیدا کردید."),
        TaskNoticeKind.DueSoon => (DueSoon, notice.TaskTitle, $"موعد فعالیت «{notice.TaskTitle}» نزدیک است: {notice.Details}"),
        TaskNoticeKind.Overdue => (Overdue, notice.TaskTitle, $"موعد فعالیت «{notice.TaskTitle}» ({notice.Details}) گذشته و هنوز انجام نشده است."),
        TaskNoticeKind.RecurringSoon => (RecurringSoon, notice.TaskTitle, $"نوبت بعدی فعالیت «{notice.TaskTitle}»: {notice.Details}"),
        _ => (Activity, notice.Action, notice.Details ?? string.Empty),
    };

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    /// <summary>A fresh scope per notification: a failed insert stays in its DbContext and must not be saved with the next.</summary>
    private async Task StoreAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(request, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Notification {Category} about task {TaskId} for user {UserId} was not stored.",
                request.Category, request.ReferenceId, request.UserId);
        }
    }
}
