using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.TaskManagement.Domain;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// When tasks are reminded of, per organization (set in the SMS panel).
/// </summary>
/// <param name="DueSoonHoursBefore">How long before a due time the "due soon" reminder goes.</param>
/// <param name="MorningHour">Tasks with a date only: the hour of the day before ("due soon") and of the day after ("overdue").</param>
/// <param name="RecurringSoonMinutesBefore">How long before each occurrence of a recurring task the "coming up" reminder goes.</param>
public sealed record TaskReminderSettingsDto(
    bool DueSoonEnabled,
    int DueSoonHoursBefore,
    int MorningHour,
    bool OverdueEnabled,
    bool RecurringSoonEnabled,
    int RecurringSoonMinutesBefore);

public interface ITaskReminderSettingsService
{
    Task<TaskReminderSettingsDto> ReadAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Result<TaskReminderSettingsDto>> GetAsync(CancellationToken cancellationToken);

    Task<Result<TaskReminderSettingsDto>> SaveAsync(TaskReminderSettingsDto request, CancellationToken cancellationToken);
}

/// <summary>One row of platform.Settings per organization.</summary>
public sealed class TaskReminderSettingsService(
    IPlatformRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IPlatformService platformService,
    ILogger<TaskReminderSettingsService> logger) : ITaskReminderSettingsService
{
    public const string SettingKey = "TaskManagement.Reminders";
    public const string SettingScope = "TaskManagement";

    /// <summary>A day before a due time (09:00 the day before a date), once when overdue, an hour before each occurrence.</summary>
    public static TaskReminderSettingsDto Defaults { get; } = new(true, 24, 9, true, true, 60);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TaskReminderSettingsDto> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var setting = await repository.FindSettingAsync(tenantId, SettingKey, SettingScope, cancellationToken);
        if (setting is null)
        {
            return Defaults;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<TaskReminderSettingsDto>(setting.Value, Json);
            return stored is not null && Validate(stored) is null ? stored : Defaults;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Task reminder settings of tenant {TenantId} are unreadable; the defaults apply.", tenantId);
            return Defaults;
        }
    }

    public async Task<Result<TaskReminderSettingsDto>> GetAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId
            ? Result.Success(await ReadAsync(tenantId, cancellationToken))
            : Result.Failure<TaskReminderSettingsDto>(Error.Unauthorized());

    public async Task<Result<TaskReminderSettingsDto>> SaveAsync(TaskReminderSettingsDto request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<TaskReminderSettingsDto>(Error.Unauthorized());
        }

        if (Validate(request) is { } problem)
        {
            return Result.Failure<TaskReminderSettingsDto>(Error.Validation(problem));
        }

        var json = JsonSerializer.Serialize(request, Json);
        var setting = await repository.FindSettingAsync(tenantId, SettingKey, SettingScope, cancellationToken);
        if (setting is null)
        {
            await repository.AddSettingAsync(new SystemSetting(Guid.NewGuid(), tenantId, SettingKey, json, SettingScope), cancellationToken);
        }
        else
        {
            setting.UpdateValue(json);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("settings.task_reminders", nameof(SystemSetting), null, json, cancellationToken);
        return Result.Success(request);
    }

    private static string? Validate(TaskReminderSettingsDto settings) =>
        settings.DueSoonHoursBefore is < 1 or > 168 ? "The due-soon reminder must be 1 to 168 hours before."
        : settings.MorningHour is < 0 or > 23 ? "The reminder hour must be 0 to 23."
        : settings.RecurringSoonMinutesBefore is < 5 or > 1440 ? "The recurring reminder must be 5 to 1440 minutes before."
        : null;
}

/// <summary>Task SMS held back during the quiet hours, sent by the reminder job when they end.</summary>
public interface ITaskSmsOutbox
{
    Task AddAsync(PendingTaskSms sms, CancellationToken cancellationToken);
}

/// <summary>
/// Sends one reminder about a task: in the app (through <see cref="ITaskNoticePublisher"/>) and by
/// SMS to those responsible (only active users with a mobile number and SMS on, each number once).
/// Failures are logged; one failed message never stops the others.
/// </summary>
public sealed class TaskReminderDispatcher(
    ITaskNoticePublisher notices,
    ITaskSmsSender smsSender,
    IUserContactResolver contacts,
    ILogger<TaskReminderDispatcher> logger)
{
    public async Task SendAsync(
        TaskItem task, TaskNoticeKind kind, string whenText,
        IReadOnlyCollection<Guid> appRecipients, IReadOnlyCollection<Guid> smsRecipients, CancellationToken cancellationToken)
    {
        if (appRecipients.Count > 0)
        {
            await notices.PublishAsync(
                new TaskNotice(task.TenantId, task.Id, task.Title, null, kind, kind.ToString(), whenText, appRecipients),
                cancellationToken);
        }

        if (smsRecipients.Count == 0)
        {
            return;
        }

        try
        {
            if (!await smsSender.IsEnabledAsync(task.TenantId, cancellationToken))
            {
                return;
            }

            var (template, values, fallback) = Sms(task, kind, whenText);
            var phones = await contacts.GetPhoneNumbersAsync(task.TenantId, smsRecipients.Distinct().ToList(), cancellationToken);
            foreach (var phone in phones.Values.Distinct(StringComparer.Ordinal))
            {
                var sent = await smsSender.SendTemplateAsync(task.TenantId, phone, template, values, fallback, cancellationToken);
                if (sent.IsFailure)
                {
                    logger.LogWarning("Task {TaskId}: {Kind} SMS not sent: {Error}", task.Id, kind, sent.Error.Message);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Task {TaskId}: {Kind} SMS failed.", task.Id, kind);
        }
    }

    private static (string Template, IReadOnlyDictionary<string, string?> Values, string Fallback) Sms(TaskItem task, TaskNoticeKind kind, string when)
    {
        switch (kind)
        {
            case TaskNoticeKind.RecurringSoon:
                var parts = when.Split(" - ", 2);
                return (TaskSmsTemplateKeys.RecurringTaskUpcoming,
                    new Dictionary<string, string?>
                    {
                        ["TaskTitle"] = task.Title,
                        ["ExecutionDateTime"] = when,
                        ["ExecutionDate"] = parts[0],
                        ["ExecutionTime"] = parts.Length > 1 ? parts[1] : null,
                    },
                    $"یادآوری: نوبت فعالیت «{task.Title}» نزدیک است.\nزمان انجام: {when}");
            case TaskNoticeKind.Overdue:
                return (TaskSmsTemplateKeys.TaskOverdue,
                    new Dictionary<string, string?> { ["TaskTitle"] = task.Title, ["DueDate"] = when },
                    $"موعد فعالیت «{task.Title}» گذشته و هنوز انجام نشده است.\nموعد: {when}");
            default:
                return (TaskSmsTemplateKeys.TaskDueSoon,
                    new Dictionary<string, string?> { ["TaskTitle"] = task.Title, ["DueDate"] = when },
                    $"یادآوری: موعد فعالیت «{task.Title}» نزدیک است.\nموعد: {when}");
        }
    }
}

/// <summary>Dates and times the way the UI shows them: Solar Hijri with Persian digits, e.g. ۱۴۰۵/۰۷/۲۳ - ۰۹:۰۰.</summary>
public static class TaskDates
{
    public static string Format(DateOnly date, TimeOnly? time = null)
    {
        var calendar = new System.Globalization.PersianCalendar();
        var value = date.ToDateTime(TimeOnly.MinValue);
        var text = $"{calendar.GetYear(value):0000}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}";
        if (time is { } t)
        {
            text += " - " + t.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        return string.Concat(text.Select(c => char.IsDigit(c) ? (char)('۰' + (c - '0')) : c));
    }
}
