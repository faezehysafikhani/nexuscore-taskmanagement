using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.TaskManagement.Application;
using Notifications.Application.Abstractions;

namespace Nexus.Integrations.TaskNotifications;

/// <summary>
/// Turns TaskManagement's due-task announcement into a NexusCore notification.
///
/// This project exists so neither module has to know the other. TaskManagement owns
/// ITaskNotificationPublisher and nothing else; Notifications owns INotificationService and
/// nothing else; this integration is the only place both names appear - the same shape
/// Nexus.Integrations.ProjectWorkflow already uses, and the reason the architecture tests can
/// keep the two business modules apart.
///
/// Leave it out of a deployment and TaskManagement still runs: its own no-op publisher takes
/// over and nothing else changes.
/// </summary>
public sealed class TaskNotificationPublisher(
    IServiceScopeFactory scopeFactory,
    ILogger<TaskNotificationPublisher> logger) : ITaskNotificationPublisher
{
    public async Task PublishAsync(TaskDueNotification notification, CancellationToken cancellationToken)
    {
        var title = $"یادآوری وظیفه: {notification.Title}";
        var when = notification.DueAtText ?? TaskCreatedChannelNotifier.ToPersianDate(notification.DueDate);
        var message = string.IsNullOrWhiteSpace(notification.Description)
            ? $"موعد انجام وظیفه «{notification.Title}» فرا رسیده است. (موعد: {when})"
            : $"موعد انجام وظیفه «{notification.Title}» فرا رسیده است. (موعد: {when}) - {notification.Description}";

        // Only the people the task names, already limited to active users of its organization.
        // Each is stored on its own, so one failure does not cost the others theirs.
        foreach (var userId in notification.RecipientUserIds)
        {
            await StoreWithRetryAsync(notification, userId, title, message, cancellationToken);
        }
    }

    /// <summary>Attempts per recipient, and the pause before each retry.</summary>
    internal static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)];

    /// <summary>
    /// Storing a notification is a database insert: a failed attempt left nothing behind, so
    /// trying again cannot create a second copy. Each attempt uses a fresh scope - a failed
    /// insert stays in its DbContext and would otherwise be saved along with the retry.
    /// </summary>
    private async Task StoreWithRetryAsync(
        TaskDueNotification notification, Guid userId, string title, string message, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<INotificationService>()
                    .NotifyAsync(userId, title, message, "Warning", cancellationToken, notification.TenantId);
                logger.LogInformation(ReminderDeliveryEvents.NotificationStored,
                    "Reminder notification stored for user {UserId}, task {TaskId}.", userId, notification.TaskId);
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ReminderDeliveryEvents.RetryPending, ex,
                    "Reminder notification for user {UserId}, task {TaskId} failed (attempt {Attempt}); trying again.",
                    userId, notification.TaskId, attempt + 1);
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ReminderDeliveryEvents.NotificationFailed, ex,
                    "Reminder notification for user {UserId} about task {TaskId} was not stored.", userId, notification.TaskId);
                return;
            }
        }
    }
}

public static class DependencyInjection
{
    /// <summary>
    /// Call after AddTaskManagement(). It replaces the module's no-op publisher, which is
    /// registered with TryAdd precisely so this can win.
    /// </summary>
    public static IServiceCollection AddTaskNotificationsIntegration(this IServiceCollection services)
    {
        services.AddScoped<ITaskNotificationPublisher, TaskNotificationPublisher>();

        // Real delivery for the module's SMS and contact seams, and SMS on task creation.
        services.AddScoped<ITaskSmsSender, PlatformTaskSmsSender>();
        services.AddScoped<IUserContactResolver, DirectoryUserContactResolver>();
        services.AddScoped<NexusCore.SharedKernel.Domain.IDomainEventHandler<Nexus.TaskManagement.Domain.TaskItemCreated>, TaskCreatedChannelNotifier>();
        return services;
    }
}
