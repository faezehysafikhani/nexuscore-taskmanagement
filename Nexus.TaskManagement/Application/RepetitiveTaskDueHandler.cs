using System.Globalization;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Identity.Interfaces;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Domain;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Reacts to a recurring task falling due: raises a notification, and an SMS when that is
/// configured.
///
/// On ordering: DomainEventDispatchInterceptor dispatches from SavedChangesAsync, which runs
/// after SaveChanges has returned. The scheduler does not open an explicit transaction, so by
/// the time this runs the schedule's new NextExecutionAtUtc is already committed. That is the
/// order we want - the schedule is advanced first, so a failure here costs at most one
/// notification and never the schedule itself.
///
/// The consequence, stated plainly: delivery is at-most-once. If notification fails the
/// occurrence is not retried, because retrying would mean holding the schedule back and
/// risking the opposite failure - a task that fires repeatedly. Anything needing at-least-once
/// delivery wants an outbox, which NexusCore does not have today.
///
/// Every failure is swallowed and logged: one unreachable recipient must not abort the rest
/// of the job's batch.
/// </summary>
public sealed class RepetitiveTaskDueHandler(
    ITaskRepository taskRepository,
    ITaskNotificationPublisher notificationPublisher,
    IUserContactResolver contactResolver,
    ITaskSmsSender smsSender,
    IUserDirectory userDirectory,
    IRecurrenceCalculator calculator,
    ILogger<RepetitiveTaskDueHandler> logger) : IDomainEventHandler<RepetitiveTaskDue>
{
    public async Task HandleAsync(RepetitiveTaskDue domainEvent, CancellationToken cancellationToken)
    {
        try
        {
            var task = await taskRepository.GetByIdAsync(domainEvent.TenantId, domainEvent.TaskId, cancellationToken);
            if (task is null)
            {
                // The task was deleted between the scan and now. Nothing to announce, and
                // certainly not a reason to fail the job.
                logger.LogInformation(
                    "Recurring task {TaskId} is due but no longer exists; nothing was sent.", domainEvent.TaskId);
                return;
            }

            var recipients = await ResolveRecipientsAsync(task, cancellationToken);
            if (recipients.Count == 0)
            {
                logger.LogInformation(
                    "Recurring task {TaskId} is due but has nobody to notify.", domainEvent.TaskId);
                return;
            }

            // The occurrence as the users entered it: their date and time, Solar Hijri.
            var occurrence = calculator.ToLocalTime(domainEvent.DueAtUtc);
            var when = FormatOccurrence(occurrence);

            // Independent: a failed notification does not stop the SMS, and the reverse.
            await PublishNotificationAsync(task, recipients, DateOnly.FromDateTime(occurrence.DateTime), when, cancellationToken);
            await SendSmsAsync(task, recipients, when, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "Failed to announce due recurring task {TaskId}.", domainEvent.TaskId);
        }
    }

    /// <summary>
    /// Everyone actually connected to the task: its assignee, its collaborators, the members of
    /// its team and its owner - and of those only active users of the task's own organization.
    /// Nobody else: a notification to an unrelated user is a bug, not a nicety.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveRecipientsAsync(TaskItem task, CancellationToken cancellationToken)
    {
        var candidates = new HashSet<Guid>();

        if (task.AssignedUserId is { } assigned)
        {
            candidates.Add(assigned);
        }

        candidates.UnionWith(task.Assignees.Select(a => a.UserId));

        if (task.AssignedUserGroupId is { } groupId)
        {
            candidates.UnionWith(await userDirectory.GetGroupMemberIdsAsync(groupId, cancellationToken));
        }

        if (task.OwnerUserId is { } owner)
        {
            candidates.Add(owner);
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var users = await userDirectory.GetUsersAsync(candidates, cancellationToken);
        var allowed = users.Where(u => u.TenantId == task.TenantId && u.IsActive).Select(u => u.Id).ToList();

        var skipped = candidates.Count - allowed.Count;
        if (skipped > 0)
        {
            logger.LogInformation(
                "Recurring task {TaskId}: {Count} linked user(s) skipped (inactive, removed or of another organization).",
                task.Id, skipped);
        }

        return allowed;
    }

    private async Task PublishNotificationAsync(
        TaskItem task, IReadOnlyList<Guid> recipients, DateOnly occurrenceDate, string when, CancellationToken cancellationToken)
    {
        try
        {
            await notificationPublisher.PublishAsync(
                new TaskDueNotification(
                    task.TenantId, task.Id, task.Title, task.Description, occurrenceDate, recipients, when),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notification failed for due task {TaskId}.", task.Id);
        }
    }

    private async Task SendSmsAsync(
        TaskItem task, IReadOnlyList<Guid> recipients, string when, CancellationToken cancellationToken)
    {
        try
        {
            if (!await smsSender.IsEnabledAsync(task.TenantId, cancellationToken))
            {
                logger.LogInformation(ReminderDeliveryEvents.SmsSkipped,
                    "Recurring task {TaskId}: SMS is turned off in the SMS panel settings; no SMS sent.", task.Id);
                return;
            }

            var phoneNumbers = await contactResolver.GetPhoneNumbersAsync(
                task.TenantId, recipients, cancellationToken);

            foreach (var userId in recipients.Where(id => !phoneNumbers.ContainsKey(id)))
            {
                logger.LogWarning(ReminderDeliveryEvents.SmsSkipped,
                    "Recurring task {TaskId}: no SMS for user {UserId} - no mobile number, or SMS notifications turned off.",
                    task.Id, userId);
            }

            // Title and time only: the description stays inside the application.
            var message = $"یادآوری وظیفه: {task.Title}\nموعد: {when}";

            foreach (var (userId, phoneNumber) in phoneNumbers)
            {
                var result = await smsSender.SendAsync(task.TenantId, phoneNumber, message, cancellationToken);
                if (result.IsFailure)
                {
                    // The gateway's reason: SMS panel incomplete, provider refused, network...
                    // Not re-sent: when the provider took the message but its answer was lost,
                    // a retry would text the user twice.
                    logger.LogWarning(ReminderDeliveryEvents.SmsFailed,
                        "SMS to user {UserId} for task {TaskId} was not sent: {Error}",
                        userId, task.Id, result.Error.Message);
                }
                else
                {
                    logger.LogInformation(ReminderDeliveryEvents.SmsAccepted,
                        "Reminder SMS for user {UserId}, task {TaskId} accepted by the SMS gateway.", userId, task.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMS delivery failed for due task {TaskId}.", task.Id);
        }
    }

    /// <summary>e.g. ۱۴۰۴/۰۷/۰۱ - ۰۹:۰۰, the way the UI shows dates.</summary>
    internal static string FormatOccurrence(DateTimeOffset local)
    {
        var calendar = new PersianCalendar();
        var date = local.DateTime;
        var text = $"{calendar.GetYear(date):0000}/{calendar.GetMonth(date):00}/{calendar.GetDayOfMonth(date):00} - {date:HH\\:mm}";
        return string.Concat(text.Select(c => char.IsDigit(c) ? (char)('۰' + (c - '0')) : c));
    }
}
