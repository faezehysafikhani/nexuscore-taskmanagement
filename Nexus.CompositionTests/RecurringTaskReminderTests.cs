using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.Integrations.TaskNotifications;
using Nexus.TaskManagement;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;
using NexusCore.Application.Common;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Messaging;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure.Messaging;
using NexusCore.Infrastructure.Persistence;
using NexusCore.SharedKernel.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Notifications.Application.Abstractions;

namespace Nexus.CompositionTests;

/// <summary>
/// The whole path of a recurring task falling due, as the host runs it: a scheduler pass on a
/// relational database (SQLite in memory - the claim is a conditional UPDATE), the
/// RepetitiveTaskDue event through DomainEventDispatchInterceptor, RepetitiveTaskDueHandler,
/// the TaskNotifications integration, and the platform SMS gateway with a recording provider
/// in place of a real one. Nothing is ever sent.
/// </summary>
public sealed class RecurringTaskReminderTests : IDisposable
{
    private static readonly TimeZoneInfo Tehran = RecurrenceCalculator.ResolveTimeZone("Asia/Tehran");

    // A database file of its own per test: every context gets its own connection, as on a server.
    private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"recurring-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _services;
    private readonly Directory _directory = new();
    private readonly RecordingNotifications _notifications = new();
    private readonly RecordingSmsProvider _smsProvider = new();
    private readonly Channels _channels = new();
    private readonly RecordingLogs _logs = new();

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();

    public RecurringTaskReminderTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_logs).SetMinimumLevel(LogLevel.Debug));

        // TaskManagement as the host registers it, plus the notification integration.
        services.AddTaskManagement();
        services.AddTaskNotificationsIntegration();
        // The SMS on task creation is another feature; these tests create tasks and count reminders only.
        services.RemoveAll<IDomainEventHandler<TaskItemCreated>>();
        services.Configure<RecurrenceOptions>(options => options.TimeZone = "Asia/Tehran");

        // Shared infrastructure the path uses; no signed-in user, as in a background job.
        services.AddSingleton<ICurrentUserContext, NoUser>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();
        services.AddSingleton<IUserDirectory>(_directory);
        services.AddSingleton<INotificationService>(_notifications);
        services.AddSingleton<INotificationChannelSettingsReader>(_channels);
        services.AddSingleton<ISmsProvider>(_smsProvider);
        services.AddScoped<ISmsSender, GatewaySmsSender>();

        services.AddDbContext<TaskManagementDbContext>((provider, options) => options
            .UseSqlite($"Data Source={_databaseFile};Pooling=False;Default Timeout=30")
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetCustomizer>()
            .AddInterceptors(provider.GetRequiredService<AuditingInterceptor>(), provider.GetRequiredService<DomainEventDispatchInterceptor>()));
        services.AddScoped<ITaskManagementUnitOfWork>(provider => provider.GetRequiredService<TaskManagementDbContext>());
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IRepetitiveTaskRepository, RepetitiveTaskRepository>();

        _services = services.BuildServiceProvider();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_databaseFile);
        }
        catch (IOException)
        {
            // A leftover temp file must not fail the run.
        }
    }

    // ---------------------------------------------------------------- scheduling

    [Fact]
    public async Task ANotYetDueSchedule_IsLeftAlone()
    {
        var owner = User("09120000001");
        var (_, scheduleId) = await RecurringTaskAsync(owner, nextRunUtc: DateTimeOffset.UtcNow.AddHours(1));

        await PassAsync();

        Assert.Empty(_notifications.Sent);
        Assert.Empty(_smsProvider.Sent);
        Assert.Null((await ScheduleAsync(scheduleId)).LastExecutionAtUtc);
    }

    [Fact]
    public async Task ADueSchedule_NotifiesAndTextsTheirPeople_AndMovesToTheNextOccurrence()
    {
        var owner = User("09120000001");
        var assignee = User("09120000002");
        var (_, scheduleId) = await RecurringTaskAsync(owner, assignedUserId: assignee.Id, startTime: new TimeOnly(9, 0));

        await PassAsync();

        Assert.Equal(new[] { owner.Id, assignee.Id }.Order(), _notifications.Sent.Select(n => n.UserId).Order());
        Assert.All(_notifications.Sent, n => Assert.Equal(_tenant, n.TenantId));
        Assert.All(_notifications.Sent, n => Assert.Contains("یادآوری وظیفه", n.Title));
        Assert.Equal(new[] { "09120000001", "09120000002" }, _smsProvider.Sent.Select(s => s.Phone).Order());
        Assert.All(_smsProvider.Sent, s => Assert.Contains("موعد: ", s.Text));

        // The next run is tomorrow at 09:00 in Tehran - not 09:00 UTC.
        var schedule = await ScheduleAsync(scheduleId);
        Assert.NotNull(schedule.LastExecutionAtUtc);
        var next = TimeZoneInfo.ConvertTime(schedule.NextExecutionAtUtc!.Value, Tehran);
        Assert.Equal(new TimeSpan(9, 0, 0), next.TimeOfDay);
        Assert.True(schedule.NextExecutionAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void TheNextOccurrence_IsTheWallClockTimeInTheConfiguredZone()
    {
        var calculator = new RecurrenceCalculator(Tehran);
        var schedule = new RepetitiveTask(Guid.NewGuid(), _tenant, Guid.NewGuid(), RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1));
        schedule.UpdateSchedule(RecurrenceFrequency.Daily, null, new TimeOnly(9, 0), null, null, null, null, null, new DateOnly(2026, 1, 1), null);

        // 23:00 UTC on 1 Jan is 02:30 on 2 Jan in Tehran: the next 09:00 there is 2 Jan 05:30 UTC.
        var next = calculator.CalculateNextExecution(schedule, new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 5, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public async Task AnInactiveSchedule_IsNotProcessed()
    {
        var owner = User("09120000001");
        var (_, scheduleId) = await RecurringTaskAsync(owner, active: false);

        await PassAsync();

        Assert.Empty(_notifications.Sent);
        Assert.Null((await ScheduleAsync(scheduleId)).LastExecutionAtUtc);
    }

    [Fact]
    public async Task AScheduleWhoseTaskIsGone_SendsNothing()
    {
        var owner = User("09120000001");
        var (taskId, scheduleId) = await RecurringTaskAsync(owner);
        using (var scope = _services.CreateScope())
        {
            // A task removed behind the application's back, leaving its schedule (the API
            // deletes both). One connection, so the pragma applies to the delete.
            var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            await db.Tasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
        }

        await PassAsync();

        Assert.Empty(_notifications.Sent);
        Assert.Empty(_smsProvider.Sent);
        Assert.Null((await ScheduleAsync(scheduleId)).LastExecutionAtUtc);
    }

    // ---------------------------------------------------------------- no duplicates

    [Fact]
    public async Task RunningThePassAgain_DoesNotRepeatTheOccurrence()
    {
        var owner = User("09120000001");
        await RecurringTaskAsync(owner);

        await PassAsync();
        await PassAsync();
        await PassAsync();

        Assert.Single(_notifications.Sent);
        Assert.Single(_smsProvider.Sent);
    }

    [Fact]
    public async Task ConcurrentPasses_OfSeparateInstances_ProcessEachOccurrenceOnce()
    {
        var owner = User("09120000001");
        await RecurringTaskAsync(owner);
        await RecurringTaskAsync(owner, title: "Second");

        // Two scheduler instances (as two app servers would have), started together.
        var first = Scheduler();
        var second = Scheduler();
        await Task.WhenAll(
            Task.Run(() => first.ProcessDueSchedulesAsync(CancellationToken.None)),
            Task.Run(() => second.ProcessDueSchedulesAsync(CancellationToken.None)));

        Assert.Equal(2, _notifications.Sent.Count);
        Assert.Equal(2, _smsProvider.Sent.Count);
    }

    // ---------------------------------------------------------------- delivery outcomes

    [Fact]
    public async Task AFailedNotification_DoesNotStopTheSms_NorTheOtherRecipients_NorTheSchedule()
    {
        var owner = User("09120000001");
        var assignee = User("09120000002");
        _notifications.FailFor.Add(owner.Id);
        var (_, scheduleId) = await RecurringTaskAsync(owner, assignedUserId: assignee.Id);

        await PassAsync();

        Assert.Equal(assignee.Id, Assert.Single(_notifications.Sent).UserId);
        Assert.Equal(2, _smsProvider.Sent.Count);
        Assert.NotNull((await ScheduleAsync(scheduleId)).LastExecutionAtUtc);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("was not stored"));
    }

    [Fact]
    public async Task AFailedSms_IsLogged_AndTheOthersStillGoOut()
    {
        var owner = User("09120000001");
        var assignee = User("09120000002");
        _smsProvider.FailFor.Add("09120000001");
        await RecurringTaskAsync(owner, assignedUserId: assignee.Id);

        await PassAsync();

        Assert.Equal("09120000002", Assert.Single(_smsProvider.Sent).Phone);
        Assert.Equal(2, _notifications.Sent.Count);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("was not sent"));
    }

    [Fact]
    public async Task AUserWithoutAPhoneNumber_GetsTheNotification_AndTheMissingNumberIsLogged()
    {
        var owner = User(phone: null);
        await RecurringTaskAsync(owner);

        await PassAsync();

        Assert.Single(_notifications.Sent);
        Assert.Empty(_smsProvider.Sent);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains($"no SMS for user {owner.Id}"));
    }

    [Fact]
    public async Task InactiveUsers_AndUsersOfAnotherOrganization_GetNothing()
    {
        var owner = User("09120000001");
        var inactive = User("09120000002", isActive: false);
        var stranger = User("09120000003", tenantId: _otherTenant);
        var (taskId, _) = await RecurringTaskAsync(owner, assignedUserId: inactive.Id);
        using (var scope = _services.CreateScope())
        {
            // A collaborator from another organization slipped in (the API refuses one).
            var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            var task = await db.Tasks.Include(t => t.Assignees).SingleAsync(t => t.Id == taskId);
            task.AssignUsers([stranger.Id]);
            await db.SaveChangesAsync();
        }

        await PassAsync();

        Assert.Equal(owner.Id, Assert.Single(_notifications.Sent).UserId);
        Assert.Equal("09120000001", Assert.Single(_smsProvider.Sent).Phone);
    }

    [Fact]
    public async Task TheTeamsMembers_AreReminded_Too()
    {
        var owner = User("09120000001");
        var member = User("09120000002");
        var teamId = Guid.NewGuid();
        _directory.Groups[teamId] = [member.Id];
        await RecurringTaskAsync(owner, assignedUserGroupId: teamId);

        await PassAsync();

        Assert.Contains(_notifications.Sent, n => n.UserId == member.Id);
        Assert.Contains(_smsProvider.Sent, s => s.Phone == "09120000002");
    }

    [Fact]
    public async Task SmsTurnedOff_SendsNoSms_ButStillNotifies()
    {
        _channels.Settings = _channels.Settings with { Enabled = false };
        var owner = User("09120000001");
        await RecurringTaskAsync(owner);

        await PassAsync();

        Assert.Single(_notifications.Sent);
        Assert.Empty(_smsProvider.Sent);
        Assert.Contains(_logs.Entries, e => e.Message.Contains("SMS is turned off"));
    }

    [Fact]
    public async Task IncompleteSmsSettings_AreReported_AndNothingIsSent()
    {
        _channels.Settings = _channels.Settings with { ApiKey = null };
        var owner = User("09120000001");
        await RecurringTaskAsync(owner);

        await PassAsync();

        Assert.Single(_notifications.Sent);
        Assert.Empty(_smsProvider.Sent);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("کلید API"));
    }

    [Fact]
    public async Task ANotificationThatFailsOnce_IsStoredOnRetry_Once()
    {
        var owner = User("09120000001");
        _notifications.FailOnceFor.Add(owner.Id);
        await RecurringTaskAsync(owner);

        await PassAsync();

        Assert.Equal(owner.Id, Assert.Single(_notifications.Sent).UserId);
        Assert.Contains(_logs.Entries, e => e.EventId == ReminderDeliveryEvents.RetryPending.Id);
        Assert.Contains(_logs.Entries, e => e.EventId == ReminderDeliveryEvents.NotificationStored.Id);
    }

    [Fact]
    public async Task EveryDeliveryState_IsLoggedWithItsOwnEvent()
    {
        var owner = User("09120000001");
        var withoutPhone = User(phone: null);
        _smsProvider.FailFor.Add("09120000001");
        await RecurringTaskAsync(owner, assignedUserId: withoutPhone.Id);

        await PassAsync();

        var ids = _logs.Entries.Select(e => e.EventId).ToHashSet();
        Assert.Contains(ReminderDeliveryEvents.OccurrenceClaimed.Id, ids);
        Assert.Contains(ReminderDeliveryEvents.NotificationStored.Id, ids);
        Assert.Contains(ReminderDeliveryEvents.SmsFailed.Id, ids);
        Assert.Contains(ReminderDeliveryEvents.SmsSkipped.Id, ids);
        // A failed SMS is never sent a second time.
        Assert.Empty(_smsProvider.Sent);
    }

    // ---------------------------------------------------------------- next runs from the old calculation

    [Fact]
    public async Task OldNextRuns_AreReportedOnly_UntilApplyIsChosen_AndThenCorrectedWithoutSendingAnything()
    {
        var owner = User("09120000001");
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        // The old calculation: 09:00 taken as UTC.
        var legacyValue = new DateTimeOffset(future.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
        var (_, legacy) = await RecurringTaskAsync(owner, nextRunUtc: legacyValue, startTime: new TimeOnly(9, 0));
        // Already correct: 09:00 in Tehran.
        var correctValue = new RecurrenceCalculator(Tehran).FromLocal(future, new TimeOnly(9, 0));
        var (_, correct) = await RecurringTaskAsync(owner, nextRunUtc: correctValue, startTime: new TimeOnly(9, 0), title: "Correct");
        // Matches neither: not touched.
        var oddValue = new DateTimeOffset(future.ToDateTime(new TimeOnly(14, 17)), TimeSpan.Zero);
        var (_, odd) = await RecurringTaskAsync(owner, nextRunUtc: oddValue, startTime: new TimeOnly(9, 0), title: "Odd");

        Assert.Equal(1, await Scheduler().RealignLegacyOccurrencesAsync("Report", CancellationToken.None));
        Assert.Equal(legacyValue, (await ScheduleAsync(legacy)).NextExecutionAtUtc);

        Assert.Equal(1, await Scheduler().RealignLegacyOccurrencesAsync("Apply", CancellationToken.None));
        var realigned = await ScheduleAsync(legacy);
        Assert.Equal(correctValue, realigned.NextExecutionAtUtc);
        Assert.Equal(new TimeOnly(9, 0), realigned.StartTime);
        Assert.Equal(RecurrenceFrequency.Daily, realigned.Frequency);
        Assert.Equal(correctValue, (await ScheduleAsync(correct)).NextExecutionAtUtc);
        Assert.Equal(oddValue, (await ScheduleAsync(odd)).NextExecutionAtUtc);
        Assert.Contains(_logs.Entries, e => e.EventId == ReminderDeliveryEvents.LegacyTimeFound.Id && e.Message.Contains(odd.ToString()));

        // Running it again finds nothing; nothing was announced along the way.
        Assert.Equal(0, await Scheduler().RealignLegacyOccurrencesAsync("Apply", CancellationToken.None));
        Assert.Empty(_notifications.Sent);
        Assert.Empty(_smsProvider.Sent);
    }

    [Fact]
    public async Task OldNextRuns_WithoutATimeOfDay_UseMidnightInTehran()
    {
        var owner = User("09120000001");
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var legacyValue = new DateTimeOffset(future.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var (_, legacy) = await RecurringTaskAsync(owner, nextRunUtc: legacyValue);

        await Scheduler().RealignLegacyOccurrencesAsync("Apply", CancellationToken.None);

        var next = TimeZoneInfo.ConvertTime((await ScheduleAsync(legacy)).NextExecutionAtUtc!.Value, Tehran);
        Assert.Equal(future.ToDateTime(TimeOnly.MinValue), next.DateTime);
    }

    // ---------------------------------------------------------------- helpers

    private UserContact User(string? phone, bool isActive = true, Guid? tenantId = null)
    {
        var user = new UserContact(Guid.NewGuid(), tenantId ?? _tenant, "User", null, null, null, phone, NotifySms: true, isActive);
        _directory.Users[user.Id] = user;
        return user;
    }

    private async Task<(Guid TaskId, Guid ScheduleId)> RecurringTaskAsync(
        UserContact owner, Guid? assignedUserId = null, Guid? assignedUserGroupId = null,
        DateTimeOffset? nextRunUtc = null, bool active = true, TimeOnly? startTime = null, string title = "Weekly report")
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();

        // The shared identity rows the task's foreign keys point at.
        foreach (var user in _directory.Users.Values.Where(u => !db.Users.Any(existing => existing.Id == u.Id)))
        {
            db.Users.Add(new User(user.Id, user.TenantId, null, user.DisplayName, "not-used"));
        }

        if (assignedUserGroupId is { } groupId && !await db.UserGroups.AnyAsync(g => g.Id == groupId))
        {
            db.UserGroups.Add(new UserGroup(groupId, _tenant, "Team " + groupId.ToString("N")[..6]));
        }

        var task = new TaskItem(Guid.NewGuid(), _tenant, title, new DateOnly(2026, 1, 1), TaskPriority.Medium, false, owner.Id, "confidential detail");
        task.UpdateDetails(title, "confidential detail", new DateOnly(2026, 1, 1), TaskPriority.Medium, assignedUserId, assignedUserGroupId, true);
        var schedule = new RepetitiveTask(Guid.NewGuid(), _tenant, task.Id, RecurrenceFrequency.Daily, new DateOnly(2026, 1, 1));
        schedule.UpdateSchedule(RecurrenceFrequency.Daily, null, startTime, null, null, null, null, null, new DateOnly(2026, 1, 1), null);
        schedule.SetNextExecution(nextRunUtc ?? DateTimeOffset.UtcNow.AddMinutes(-1));
        if (!active)
        {
            schedule.Deactivate();
        }

        db.Tasks.Add(task);
        db.RepetitiveTasks.Add(schedule);
        await db.SaveChangesAsync();
        return (task.Id, schedule.Id);
    }

    private RepetitiveTaskSchedulerService Scheduler() => new(
        _services,
        Options.Create(new RepetitiveTaskSchedulerOptions { Enabled = true }),
        _services.GetRequiredService<ILogger<RepetitiveTaskSchedulerService>>());

    private Task PassAsync() => Scheduler().ProcessDueSchedulesAsync(CancellationToken.None);

    private async Task<RepetitiveTask> ScheduleAsync(Guid id)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>().RepetitiveTasks
            .IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == id);
    }

    /// <summary>
    /// SQLite cannot compare DateTimeOffset values; stored as UTC ticks they compare exactly. The
    /// shared identity tables (owned by NexusCoreDbContext in production) are created here too.
    /// </summary>
    private sealed class SqliteDateTimeOffsetCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.IsTableExcludedFromMigrations()))
            {
                entityType.SetIsTableExcludedFromMigrations(false);
            }

            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            }
        }
    }

    private sealed class NoUser : ICurrentUserContext
    {
        public Guid? UserId => null;
        public Guid? TenantId => null;
        public string? Email => null;
        public string? IpAddress => null;
    }

    private sealed class Directory : IUserDirectory
    {
        public ConcurrentDictionary<Guid, UserContact> Users { get; } = new();
        public ConcurrentDictionary<Guid, Guid[]> Groups { get; } = new();

        public Task<IReadOnlyList<UserContact>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserContact>>(userIds.Where(Users.ContainsKey).Select(id => Users[id]).ToList());

        public Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(Guid groupId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Groups.TryGetValue(groupId, out var members) ? members : []);

        public Task<IReadOnlyList<Guid>> GetGroupIdsOfUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Groups.Where(g => g.Value.Contains(userId)).Select(g => g.Key).ToList());
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public ConcurrentQueue<(Guid UserId, Guid? TenantId, string Title, string Message)> Queue { get; } = new();
        public IReadOnlyList<(Guid UserId, Guid? TenantId, string Title, string Message)> Sent => Queue.ToList();
        public HashSet<Guid> FailFor { get; } = [];
        public HashSet<Guid> FailOnceFor { get; } = [];

        public Task NotifyAsync(Guid userId, string title, string message, string type, CancellationToken cancellationToken = default, Guid? tenantId = null)
        {
            if (FailFor.Contains(userId) || FailOnceFor.Remove(userId))
            {
                throw new InvalidOperationException("Notification store unavailable.");
            }

            Queue.Enqueue((userId, tenantId, title, message));
            return Task.CompletedTask;
        }
    }

    private sealed class Channels : INotificationChannelSettingsReader
    {
        public SmsChannelSettingsDto Settings { get; set; } = new(true, "test", null, "test-key-from-settings", "3000");

        public Task<NotificationChannelSettingsDto> ReadAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(new NotificationChannelSettingsDto(Settings));
    }

    /// <summary>Stands in for Kavenegar: records instead of sending.</summary>
    private sealed class RecordingSmsProvider : ISmsProvider
    {
        public ConcurrentQueue<(string Phone, string Text)> Queue { get; } = new();
        public IReadOnlyList<(string Phone, string Text)> Sent => Queue.ToList();
        public HashSet<string> FailFor { get; } = [];
        public string Key => "test";
        public string DisplayName => "Test";
        public string DefaultBaseUrl => "https://sms.invalid";

        public Task<Result<string>> SendAsync(SmsProviderSettings settings, string phoneNumber, string text, CancellationToken cancellationToken)
        {
            if (FailFor.Contains(phoneNumber))
            {
                return Task.FromResult(Result.Failure<string>(Error.Validation("سرویس‌دهنده پیامک را نپذیرفت.")));
            }

            Queue.Enqueue((phoneNumber, text));
            return Task.FromResult(Result.Success(Guid.NewGuid().ToString()));
        }
    }

    private sealed class RecordingLogs : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, int EventId, string Message)> Queue { get; } = new();
        public IReadOnlyList<(LogLevel Level, int EventId, string Message)> Entries => Queue.ToList();
        public ILogger CreateLogger(string categoryName) => new Logger(this);
        public void Dispose() { }

        private sealed class Logger(RecordingLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Queue.Enqueue((logLevel, eventId.Id, formatter(state, exception)));
        }
    }
}
