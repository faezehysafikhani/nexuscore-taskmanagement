using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;

namespace Nexus.CompositionTests;

/// <summary>
/// The notification outbox (RC-07 / OPEN-04): a notification that fails is retried, and a notification is never
/// delivered twice. SQLite in a file of its own (the claim is a conditional UPDATE), a clock the tests move and a
/// publisher that fails when told to.
/// </summary>
public sealed class TaskNotificationOutboxTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"outbox-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _services;
    private readonly Clock _clock = new();
    private readonly Publisher _publisher = new();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _task = Guid.NewGuid();
    private readonly Guid _anna = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    public TaskNotificationOutboxTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(_publisher);
        services.AddScoped<ITaskNotificationPublisher>(p => p.GetRequiredService<Publisher>());
        services.AddDbContext<TaskManagementDbContext>(o => o
            .UseSqlite($"Data Source={_file};Pooling=False;Default Timeout=30")
            .ReplaceService<IModelCustomizer, SqliteCustomizer>());
        services.AddSingleton<ITaskNotificationOutbox>(p => new TaskNotificationOutbox(
            p.GetRequiredService<IServiceScopeFactory>(), _clock, NullLogger<TaskNotificationOutbox>.Instance));
        _services = services.BuildServiceProvider();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch (IOException) { }
    }

    private ITaskNotificationOutbox Outbox => _services.GetRequiredService<ITaskNotificationOutbox>();

    private TaskDueNotification Notice(params Guid[] people) =>
        new(_tenant, _task, "Report", "Monthly", new DateOnly(2026, 11, 10), people, "1405/08/19 09:00");

    private async Task<List<PendingTaskNotification>> RowsAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>().NotificationOutbox.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task ANotification_IsDeliveredOnce_OneRowPerPerson()
    {
        var ids = await Outbox.EnqueueAsync(Notice(_anna, _bob), "due:1", default);
        Assert.Equal(2, ids.Count);

        Assert.Equal(2, await Outbox.DeliverAsync(ids, default));
        Assert.Equal(0, await Outbox.DeliverAsync(null, default)); // nothing left to send
        Assert.Equal(2, _publisher.Delivered.Count);
        Assert.All(_publisher.Delivered, n => Assert.Single(n.RecipientUserIds));
        Assert.All(await RowsAsync(), r => Assert.NotNull(r.DeliveredAtUtc));
    }

    [Fact]
    public async Task AFailedNotification_IsRetriedLater_AndStillDeliveredOnlyOnce()
    {
        _publisher.Fail = true;
        var ids = await Outbox.EnqueueAsync(Notice(_anna), "due:1", default);

        Assert.Equal(0, await Outbox.DeliverAsync(ids, default));
        var row = Assert.Single(await RowsAsync());
        Assert.Null(row.DeliveredAtUtc);
        Assert.Equal(1, row.Attempts);
        Assert.True(row.NextAttemptAtUtc > _clock.Now); // waits before the next try

        // Too early: not tried again.
        Assert.Equal(0, await Outbox.DeliverAsync(null, default));
        Assert.Equal(1, _publisher.Calls);

        // The notification service is back, and the wait is over.
        _publisher.Fail = false;
        _clock.Now = _clock.Now.AddMinutes(2);
        Assert.Equal(1, await Outbox.DeliverAsync(null, default));
        Assert.Equal(1, await Task.FromResult(_publisher.Delivered.Count));

        _clock.Now = _clock.Now.AddHours(2);
        Assert.Equal(0, await Outbox.DeliverAsync(null, default));
        Assert.Single(_publisher.Delivered);
    }

    [Fact]
    public async Task TheSameOccurrence_IsQueuedOnce_ADifferentOneIsNot()
    {
        Assert.Single(await Outbox.EnqueueAsync(Notice(_anna), "due:1", default));
        Assert.Empty(await Outbox.EnqueueAsync(Notice(_anna), "due:1", default)); // a retry of the same turn
        Assert.Single(await Outbox.EnqueueAsync(Notice(_anna), "due:2", default)); // the next turn

        Assert.Equal(2, await Outbox.DeliverAsync(null, default));
        Assert.Equal(2, _publisher.Delivered.Count);
    }

    [Fact]
    public async Task OnlyThePersonWhoseNotificationFailed_IsRetried()
    {
        _publisher.FailFor = _bob;
        var ids = await Outbox.EnqueueAsync(Notice(_anna, _bob), "due:1", default);
        Assert.Equal(1, await Outbox.DeliverAsync(ids, default));
        Assert.Equal(_anna, Assert.Single(_publisher.Delivered).RecipientUserIds.Single());

        _publisher.FailFor = null;
        _clock.Now = _clock.Now.AddMinutes(2);
        Assert.Equal(1, await Outbox.DeliverAsync(null, default));
        Assert.Equal(2, _publisher.Delivered.Count);
        Assert.Equal(1, _publisher.Delivered.Count(n => n.RecipientUserIds.Single() == _anna)); // Anna was not told again
    }

    [Fact]
    public async Task AfterTheLastAttempt_ARowIsLeftAlone_WithTheReasonKept()
    {
        _publisher.Fail = true;
        await Outbox.EnqueueAsync(Notice(_anna), "due:1", default);
        for (var i = 0; i < PendingTaskNotification.MaxAttempts + 3; i++)
        {
            await Outbox.DeliverAsync(null, default);
            _clock.Now = _clock.Now.AddHours(2);
        }

        var row = Assert.Single(await RowsAsync());
        Assert.Equal(PendingTaskNotification.MaxAttempts, row.Attempts);
        Assert.Null(row.DeliveredAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(row.LastError));
        Assert.Equal(PendingTaskNotification.MaxAttempts, _publisher.Calls);
    }

    [Fact]
    public async Task ARowClaimedByAnotherServer_IsNotSentAgain()
    {
        var ids = await Outbox.EnqueueAsync(Notice(_anna), "due:1", default);
        var first = Outbox.DeliverAsync(ids, default);
        var second = Outbox.DeliverAsync(ids, default);
        await Task.WhenAll(first, second);
        Assert.Single(_publisher.Delivered);
    }

    private sealed class Publisher : IReliableTaskNotificationPublisher
    {
        private readonly object _lock = new();
        public bool Fail { get; set; }
        public Guid? FailFor { get; set; }
        public int Calls { get; private set; }
        public List<TaskDueNotification> Delivered { get; } = [];

        public Task PublishAsync(TaskDueNotification notification, CancellationToken cancellationToken) =>
            TryPublishAsync(notification, cancellationToken);

        public Task<bool> TryPublishAsync(TaskDueNotification notification, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Calls++;
                if (Fail || (FailFor is { } who && notification.RecipientUserIds.Contains(who)))
                {
                    return Task.FromResult(false);
                }

                Delivered.Add(notification);
                return Task.FromResult(true);
            }
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 10, 8, 30, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>SQLite cannot compare DateTimeOffset; stored as UTC ticks they compare exactly. Identity tables are created too.</summary>
    private sealed class SqliteCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
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
}
