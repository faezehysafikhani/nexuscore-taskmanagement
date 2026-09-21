using Microsoft.Extensions.Logging;
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

            var recipients = ResolveRecipients(task);
            if (recipients.Count == 0)
            {
                logger.LogInformation(
                    "Recurring task {TaskId} is due but has nobody to notify.", domainEvent.TaskId);
                return;
            }

            await PublishNotificationAsync(task, recipients, cancellationToken);
            await SendSmsAsync(task, recipients, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "Failed to announce due recurring task {TaskId}.", domainEvent.TaskId);
        }
    }

    /// <summary>
    /// Everyone actually connected to the task: its assignee, its collaborators and its owner.
    /// Nobody else - a notification to an unrelated user is a bug, not a nicety.
    /// </summary>
    private static List<Guid> ResolveRecipients(TaskItem task)
    {
        var recipients = new List<Guid>();

        if (task.AssignedUserId is { } assigned)
        {
            recipients.Add(assigned);
        }

        recipients.AddRange(task.Assignees.Select(a => a.UserId));

        if (task.OwnerUserId is { } owner)
        {
            recipients.Add(owner);
        }

        return recipients.Distinct().ToList();
    }

    private async Task PublishNotificationAsync(
        TaskItem task, IReadOnlyList<Guid> recipients, CancellationToken cancellationToken)
    {
        try
        {
            await notificationPublisher.PublishAsync(
                new TaskDueNotification(
                    task.TenantId, task.Id, task.Title, task.Description, task.DueDate, recipients),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notification failed for due task {TaskId}.", task.Id);
        }
    }

    private async Task SendSmsAsync(
        TaskItem task, IReadOnlyList<Guid> recipients, CancellationToken cancellationToken)
    {
        if (!await smsSender.IsEnabledAsync(task.TenantId, cancellationToken))
        {
            return;
        }

        try
        {
            var phoneNumbers = await contactResolver.GetPhoneNumbersAsync(
                task.TenantId, recipients, cancellationToken);

            if (phoneNumbers.Count == 0)
            {
                logger.LogInformation(
                    "SMS is enabled but no phone number is available for any recipient of task {TaskId}.", task.Id);
                return;
            }

            var message = $"یادآوری وظیفه: {task.Title} - موعد: {task.DueDate:yyyy-MM-dd}";

            foreach (var (userId, phoneNumber) in phoneNumbers)
            {
                var result = await smsSender.SendAsync(task.TenantId, phoneNumber, message, cancellationToken);
                if (result.IsFailure)
                {
                    logger.LogWarning(
                        "SMS to user {UserId} for task {TaskId} was not sent: {Error}",
                        userId, task.Id, result.Error.Message);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMS delivery failed for due task {TaskId}.", task.Id);
        }
    }
}
