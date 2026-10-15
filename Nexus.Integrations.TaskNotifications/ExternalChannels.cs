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
///
/// A template turned off in the SMS panel is not sent. During the organization's quiet hours a
/// task SMS is rendered and kept (<see cref="ITaskSmsOutbox"/>); the reminder job sends it when
/// they end. Without the outbox or the quiet hours (a host that has neither) it is sent at once.
/// </summary>
public sealed class PlatformTaskSmsSender(
    ISmsSender smsSender,
    INotificationChannelSettingsReader settings,
    ISmsTemplateService templates,
    ISmsQuietHours? quietHours = null,
    ITaskSmsOutbox? outbox = null,
    TimeProvider? clock = null,
    ILogger<PlatformTaskSmsSender>? logger = null) : ITaskSmsSender
{
    public async Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await settings.ReadAsync(tenantId, cancellationToken)).Sms.Enabled;

    public async Task<Result> SendAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken)
    {
        var sent = await smsSender.SendAsync(tenantId, phoneNumber, message, cancellationToken);
        return sent.IsSuccess ? Result.Success() : Result.Failure(sent.Error);
    }

    /// <summary>The tenant's wording of the template (SMS panel), filled in and sent - or kept for after the quiet hours.</summary>
    public async Task<Result> SendTemplateAsync(
        Guid tenantId, string phoneNumber, string templateKey, IReadOnlyDictionary<string, string?> values,
        string fallbackText, CancellationToken cancellationToken)
    {
        if (!await templates.IsEnabledAsync(tenantId, templateKey, cancellationToken))
        {
            return Result.Failure(Error.Validation(SmsTemplateService.TurnedOffMessage));
        }

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (quietHours is not null && outbox is not null
            && await quietHours.QuietUntilAsync(tenantId, now, cancellationToken) is { } until)
        {
            var text = await templates.RenderAsync(tenantId, templateKey, values, cancellationToken);
            if (text.IsFailure)
            {
                return Result.Failure(text.Error);
            }

            try
            {
                await outbox.AddAsync(new PendingTaskSms(Guid.NewGuid(), tenantId, phoneNumber, text.Value!, until, now), cancellationToken);
                return Result.Success();
            }
            catch (Exception ex)
            {
                // Better at night than never.
                logger?.LogError(ex, "A task SMS could not be kept for after the quiet hours; it is sent now.");
            }
        }

        var sent = await templates.SendAsync(tenantId, phoneNumber, templateKey, values, cancellationToken);
        return sent.IsSuccess ? Result.Success() : Result.Failure(sent.Error);
    }
}

/// <summary>TaskManagement's SMS templates, registered with the platform's SMS panel.</summary>
public sealed class TaskSmsTemplateCatalog : ISmsTemplateCatalog
{
    public IReadOnlyList<SmsTemplateDefinition> GetTemplates() =>
    [
        new(TaskSmsTemplateKeys.TaskAssigned, "ارجاع فعالیت", ["TaskTitle", "DueDate", "CreatorName"],
            "فعالیت جدیدی با عنوان «{TaskTitle}» به شما ارجاع شد.\nموعد انجام: {DueDate}",
            "وقتی فعالیتی ثبت می‌شود یا مسئول اجرای آن عوض می‌شود، فقط برای مسئول اجرا ارسال می‌شود (نه برای کسی که خودش آن را ثبت یا تغییر داده).",
            ["TaskTitle"]),
        new(TaskSmsTemplateKeys.RecurringTaskReminder, "یادآوری فعالیت تکرارشونده", ["TaskTitle", "ExecutionDateTime", "ExecutionDate", "ExecutionTime"],
            "یادآوری فعالیت تکرارشونده:\n{TaskTitle}\nزمان انجام: {ExecutionDateTime}",
            "در زمان هر نوبت فعالیت تکرارشونده، فقط برای مسئول اجرای آن ارسال می‌شود (هر نوبت، حتی اگر نوبت قبلی انجام شده باشد).",
            ["TaskTitle"]),
        new(TaskSmsTemplateKeys.TaskDueChanged, "تغییر موعد فعالیت", ["TaskTitle", "DueDate"],
            "موعد فعالیت «{TaskTitle}» تغییر کرد.\nموعد جدید: {DueDate}",
            "وقتی تاریخ یا ساعت موعد فعالیت واقعاً تغییر کند، برای مسئول اجرا ارسال می‌شود (نه وقتی خودش آن را تغییر داده باشد).",
            ["TaskTitle"]),
        new(TaskSmsTemplateKeys.TaskDueSoon, "یادآوری موعد فعالیت", ["TaskTitle", "DueDate"],
            "یادآوری: موعد فعالیت «{TaskTitle}» نزدیک است.\nموعد: {DueDate}",
            "پیش از رسیدن موعد فعالیتی که هنوز انجام نشده (طبق «زمان‌بندی یادآوری‌ها»)، برای مسئول اجرا ارسال می‌شود.",
            ["TaskTitle"]),
        new(TaskSmsTemplateKeys.TaskOverdue, "گذشتن موعد فعالیت", ["TaskTitle", "DueDate"],
            "موعد فعالیت «{TaskTitle}» گذشته و هنوز انجام نشده است.\nموعد: {DueDate}",
            "یک بار، وقتی موعد فعالیت بگذرد و هنوز انجام نشده یا متوقف نشده باشد، برای مسئول اجرا ارسال می‌شود.",
            ["TaskTitle"]),
        new(TaskSmsTemplateKeys.RecurringTaskUpcoming, "یادآوری پیش از نوبت تکرارشونده", ["TaskTitle", "ExecutionDateTime", "ExecutionDate", "ExecutionTime"],
            "یادآوری: نوبت فعالیت «{TaskTitle}» نزدیک است.\nزمان انجام: {ExecutionDateTime}",
            "پیش از هر نوبت فعالیت تکرارشونده (طبق «زمان‌بندی یادآوری‌ها»)، برای مسئول اجرا ارسال می‌شود.",
            ["TaskTitle"]),
    ];
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
/// When a task is created, tells each of its responsible people by SMS with the "task_assigned"
/// template - only them: not people on its access list or team members, and not the creator, who
/// knows the task is theirs (also when they made themselves responsible). Each gets it once.
///
/// Runs after the task is saved and in the background, so a slow or unreachable gateway
/// never delays or fails the request that created the task. Delivery is best-effort: a failed
/// message is logged, not retried.
/// </summary>
public sealed class TaskCreatedChannelNotifier(IServiceScopeFactory scopeFactory) : IDomainEventHandler<TaskItemCreated>
{
    public Task HandleAsync(TaskItemCreated domainEvent, CancellationToken cancellationToken)
    {
        _ = Task.Run(() => ResponsibleSms.SendAsync(
            scopeFactory, domainEvent.TenantId, domainEvent.TaskId, TaskSmsTemplateKeys.TaskAssigned,
            task => task.ResponsibleUserIds, skipUserId: null, skipCreator: true));
        return Task.CompletedTask;
    }

    /// <summary>Solar Hijri date with Persian digits, as the UI shows it (e.g. ۱۴۰۴/۰۷/۰۱).</summary>
    internal static string ToPersianDate(DateOnly date)
    {
        var calendar = new PersianCalendar();
        var value = date.ToDateTime(TimeOnly.MinValue);
        var text = $"{calendar.GetYear(value):0000}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}";
        return ToPersianDigits(text);
    }

    internal static string ToPersianDigits(string text) =>
        string.Concat(text.Select(c => char.IsDigit(c) ? (char)('۰' + (c - '0')) : c));
}

/// <summary>
/// After an edit: each person newly responsible for a task gets "task_assigned"; those who stayed
/// responsible while the due date moved get "task_due_changed". Nobody is texted about their own
/// change, and a recurring task's date (its schedule start) is not announced - its occurrences are.
/// </summary>
public sealed class TaskChangeSmsNotifier(IServiceScopeFactory scopeFactory)
    : IDomainEventHandler<TaskItemAssigneeChanged>, IDomainEventHandler<TaskItemDueChanged>
{
    public Task HandleAsync(TaskItemAssigneeChanged domainEvent, CancellationToken cancellationToken)
    {
        _ = Task.Run(() => ResponsibleSms.SendAsync(
            scopeFactory, domainEvent.TenantId, domainEvent.TaskId, TaskSmsTemplateKeys.TaskAssigned,
            _ => [domainEvent.AssignedUserId], domainEvent.ChangedByUserId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(TaskItemDueChanged domainEvent, CancellationToken cancellationToken)
    {
        _ = Task.Run(() => ResponsibleSms.SendAsync(
            scopeFactory, domainEvent.TenantId, domainEvent.TaskId, TaskSmsTemplateKeys.TaskDueChanged,
            task => domainEvent.ResponsibleUserIds ?? task.ResponsibleUserIds, domainEvent.ChangedByUserId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// One task SMS to each of the given people who is (still) responsible for the task, and to
/// nobody else: an active user of the task's organization with a mobile number and SMS
/// notifications on (IUserContactResolver). Each person - and each phone number - once.
/// </summary>
internal static class ResponsibleSms
{
    public static async Task SendAsync(
        IServiceScopeFactory scopeFactory, Guid tenantId, Guid taskId, string templateKey,
        Func<TaskItem, IEnumerable<Guid>> recipients, Guid? skipUserId, bool skipCreator = false)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ResponsibleSms));

        try
        {
            var sms = services.GetRequiredService<ITaskSmsSender>();
            if (!await sms.IsEnabledAsync(tenantId, CancellationToken.None))
            {
                return;
            }

            var task = await services.GetRequiredService<ITaskRepository>().GetByIdAsync(tenantId, taskId, CancellationToken.None);
            if (task is null)
            {
                return;
            }

            if (templateKey == TaskSmsTemplateKeys.TaskDueChanged && task.Recurrence is not null)
            {
                return;
            }

            var responsible = task.ResponsibleUserIds;
            var creatorId = skipCreator ? task.OwnerUserId : null;
            var userIds = recipients(task).Distinct()
                .Where(id => id != skipUserId && id != creatorId && responsible.Contains(id))
                .ToList();
            if (userIds.Count == 0)
            {
                return;
            }

            var phones = await services.GetRequiredService<IUserContactResolver>()
                .GetPhoneNumbersAsync(tenantId, userIds, CancellationToken.None);
            foreach (var skipped in userIds.Where(id => !phones.ContainsKey(id)))
            {
                logger.LogInformation("Task {TaskId}: no SMS for user {UserId} - inactive, no mobile number, or SMS notifications off.", task.Id, skipped);
            }

            string? creator = null;
            if (task.OwnerUserId is { } ownerId)
            {
                var owners = await services.GetRequiredService<IUserDirectory>().GetUsersAsync([ownerId], CancellationToken.None);
                creator = owners.FirstOrDefault(u => u.TenantId == tenantId)?.DisplayName;
            }

            var due = TaskCreatedChannelNotifier.ToPersianDate(task.DueDate);
            if (task.DueTime is { } dueTime)
            {
                due += " - " + TaskCreatedChannelNotifier.ToPersianDigits(dueTime.ToString("HH:mm", CultureInfo.InvariantCulture));
            }

            var values = new Dictionary<string, string?> { ["TaskTitle"] = task.Title, ["DueDate"] = due, ["CreatorName"] = creator };
            var fallback = templateKey == TaskSmsTemplateKeys.TaskDueChanged
                ? $"موعد فعالیت «{task.Title}» تغییر کرد.\nموعد جدید: {due}"
                : $"فعالیت جدیدی با عنوان «{task.Title}» به شما ارجاع شد.\nموعد انجام: {due}";

            var texted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (userId, phoneNumber) in userIds.Where(phones.ContainsKey).Select(id => (id, phones[id])))
            {
                if (!texted.Add(phoneNumber))
                {
                    continue;
                }

                var sent = await sms.SendTemplateAsync(tenantId, phoneNumber, templateKey, values, fallback, CancellationToken.None);
                if (sent.IsFailure)
                {
                    logger.LogWarning("Task {TaskId}: SMS {Template} to user {UserId} not sent: {Error}", task.Id, templateKey, userId, sent.Error.Message);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Task SMS {Template} for task {TaskId} failed.", templateKey, taskId);
        }
    }
}
