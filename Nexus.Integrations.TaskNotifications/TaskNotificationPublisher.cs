using Microsoft.Extensions.DependencyInjection;
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
public sealed class TaskNotificationPublisher(INotificationService notifications) : ITaskNotificationPublisher
{
    public async Task PublishAsync(TaskDueNotification notification, CancellationToken cancellationToken)
    {
        var title = $"یادآوری وظیفه: {notification.Title}";
        var message = string.IsNullOrWhiteSpace(notification.Description)
            ? $"موعد انجام وظیفه «{notification.Title}» فرا رسیده است. (تاریخ: {notification.DueDate:yyyy-MM-dd})"
            : $"موعد انجام وظیفه «{notification.Title}» فرا رسیده است. (تاریخ: {notification.DueDate:yyyy-MM-dd}) - {notification.Description}";

        // Only the people the task named. RecipientUserIds is already de-duplicated and
        // limited to the assignee, the collaborators and the owner.
        foreach (var userId in notification.RecipientUserIds)
        {
            await notifications.NotifyAsync(userId, title, message, "Warning", cancellationToken);
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
