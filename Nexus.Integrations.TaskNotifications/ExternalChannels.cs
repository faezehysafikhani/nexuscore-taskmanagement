using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Messaging;
using NexusCore.SharedKernel.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.TaskNotifications;

/// <summary>
/// TaskManagement's SMS seam, backed by the platform SMS gateway (tenant notification-channel
/// settings). Replaces the module's logging-only default.
/// </summary>
public sealed class PlatformTaskSmsSender(
    ISmsSender smsSender,
    INotificationChannelSettingsReader settings) : ITaskSmsSender
{
    public async Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await settings.ReadAsync(tenantId, cancellationToken)).Sms.Enabled;

    public async Task<Result> SendAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken)
    {
        var sent = await smsSender.SendAsync(tenantId, phoneNumber, message, cancellationToken);
        return sent.IsSuccess ? Result.Success() : Result.Failure(sent.Error);
    }
}

/// <summary>
/// Phone numbers from the shared identity users. A user who switched SMS off, or has no
/// number, is simply not in the result.
/// </summary>
public sealed class DirectoryUserContactResolver(IUserDirectory directory) : IUserContactResolver
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetPhoneNumbersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var users = await directory.GetUsersAsync(userIds, cancellationToken);
        return users
            .Where(u => u.TenantId == tenantId && u.IsActive && u.NotifySms && !string.IsNullOrWhiteSpace(u.PhoneNumber))
            .ToDictionary(u => u.Id, u => u.PhoneNumber!);
    }
}

/// <summary>
/// When a task is created, tells the people involved by SMS - the assignee, the collaborators,
/// the members of the assigned team and the creator - each according to their own notification
/// preference and the tenant's SMS gateway settings.
///
/// Runs after the task is saved and in the background, so a slow or unreachable gateway
/// never delays or fails the request that created the task. Delivery is best-effort: a failed
/// message is logged, not retried.
/// </summary>
public sealed class TaskCreatedChannelNotifier(IServiceScopeFactory scopeFactory) : IDomainEventHandler<TaskItemCreated>
{
    public Task HandleAsync(TaskItemCreated domainEvent, CancellationToken cancellationToken)
    {
        _ = Task.Run(() => NotifyAsync(domainEvent));
        return Task.CompletedTask;
    }

    private async Task NotifyAsync(TaskItemCreated created)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<TaskCreatedChannelNotifier>>();

        try
        {
            var channels = await services.GetRequiredService<INotificationChannelSettingsReader>()
                .ReadAsync(created.TenantId, CancellationToken.None);
            if (!channels.Sms.Enabled)
            {
                return;
            }

            var tasks = services.GetRequiredService<ITaskRepository>();
            var task = await tasks.GetByIdAsync(created.TenantId, created.TaskId, CancellationToken.None);
            if (task is null)
            {
                return;
            }

            var directory = services.GetRequiredService<IUserDirectory>();
            var recipientIds = new HashSet<Guid>();
            if (task.AssignedUserId is { } assigned) recipientIds.Add(assigned);
            foreach (var assignee in task.Assignees) recipientIds.Add(assignee.UserId);
            if (task.AssignedUserGroupId is { } groupId)
            {
                foreach (var member in await directory.GetGroupMemberIdsAsync(groupId, CancellationToken.None)) recipientIds.Add(member);
            }
            if (task.OwnerUserId is { } owner) recipientIds.Add(owner);

            if (recipientIds.Count == 0)
            {
                return;
            }

            var users = (await directory.GetUsersAsync(recipientIds, CancellationToken.None))
                .Where(u => u.TenantId == created.TenantId && u.IsActive)
                .ToList();
            var byId = users.ToDictionary(u => u.Id);

            string? groupName = null;
            if (task.AssignedUserGroupId is { } gid)
            {
                var groups = await tasks.GetUserGroupSummariesAsync([gid], CancellationToken.None);
                groupName = groups.TryGetValue(gid, out var g) ? g.Name : null;
            }

            var creator = task.OwnerUserId is { } o && byId.TryGetValue(o, out var ownerUser) ? ownerUser.DisplayName : "همکار";
            var assigneeName = task.AssignedUserId is { } a && byId.TryGetValue(a, out var assignedUser)
                ? assignedUser.DisplayName
                : groupName ?? "شما / تیم شما";
            var priority = task.Priority switch
            {
                TaskPriority.High => "بالا",
                TaskPriority.Medium => "متوسط",
                _ => "پایین"
            };
            var due = ToPersianDate(task.DueDate);
            if (task.DueTime is { } dueTime)
            {
                due += " - " + ToPersianDigits(dueTime.ToString("HH:mm", CultureInfo.InvariantCulture));
            }

            var smsText = $"📋 فعالیت جدید \"{task.Title}\" به {assigneeName} واگذار شد.\nایجادکننده: {creator}\nاولویت: {priority} | مهلت: {due}";

            var sms = services.GetRequiredService<ISmsSender>();

            foreach (var user in users)
            {
                if (user.NotifySms && !string.IsNullOrWhiteSpace(user.PhoneNumber))
                {
                    var sent = await sms.SendAsync(created.TenantId, user.PhoneNumber!, smsText, CancellationToken.None);
                    if (sent.IsFailure)
                    {
                        logger.LogWarning("Task {TaskId}: SMS to user {UserId} not sent: {Error}", task.Id, user.Id, sent.Error.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Task-created notifications for task {TaskId} failed.", created.TaskId);
        }
    }

    /// <summary>Solar Hijri date with Persian digits, as the UI shows it (e.g. ۱۴۰۴/۰۷/۰۱).</summary>
    internal static string ToPersianDate(DateOnly date)
    {
        var calendar = new PersianCalendar();
        var value = date.ToDateTime(TimeOnly.MinValue);
        var text = $"{calendar.GetYear(value):0000}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}";
        return ToPersianDigits(text);
    }

    private static string ToPersianDigits(string text) =>
        string.Concat(text.Select(c => char.IsDigit(c) ? (char)('۰' + (c - '0')) : c));
}
