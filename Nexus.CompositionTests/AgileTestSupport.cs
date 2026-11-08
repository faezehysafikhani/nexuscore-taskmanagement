using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

internal sealed class FakeAgileTaskRepository : IAgileTaskRepository
{
    public List<AgileTask> Items { get; } = [];
    public Task<AgileTask?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(t => t.Id == id));
    public Task<IReadOnlyList<AgileTask>> ListByProjectAsync(Guid projectId, int? sprintNumber, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgileTask>>(Items.Where(t => t.ProjectId == projectId && (sprintNumber is null || t.SprintNumber == sprintNumber)).ToList());
    public Task<IReadOnlyList<AgileTask>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgileTask>>(Items.Where(t => ids.Contains(t.Id)).ToList());
    public Task AddAsync(AgileTask task, CancellationToken ct) { Items.Add(task); return Task.CompletedTask; }
    public Task RemoveAsync(AgileTask task, CancellationToken ct) { Items.Remove(task); return Task.CompletedTask; }
}

internal sealed class FakeSprintRepository : ISprintRepository
{
    public List<Sprint> Items { get; } = [];
    public Task<Sprint?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(s => s.Id == id));
    public Task<Sprint?> GetByNumberAsync(Guid projectId, int number, CancellationToken ct) =>
        Task.FromResult(Items.SingleOrDefault(s => s.ProjectId == projectId && s.Number == number));
    public Task<IReadOnlyList<Sprint>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Sprint>>(Items.Where(s => s.ProjectId == projectId).OrderBy(s => s.Number).ToList());
    public Task AddAsync(Sprint sprint, CancellationToken ct) { Items.Add(sprint); return Task.CompletedTask; }
    public Task RemoveAsync(Sprint sprint, CancellationToken ct) { Items.Remove(sprint); return Task.CompletedTask; }
}

internal sealed class FakeSprintEventRepository : ISprintEventRepository
{
    public List<SprintEvent> Items { get; } = [];
    public Task<IReadOnlyList<SprintEvent>> ListBySprintAsync(Guid projectId, int sprintNumber, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SprintEvent>>(Items.Where(e => e.ProjectId == projectId && e.SprintNumber == sprintNumber).OrderBy(e => e.OccurredAtUtc).ToList());
    public Task<IReadOnlyList<SprintEvent>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SprintEvent>>(Items.Where(e => e.ProjectId == projectId).OrderBy(e => e.OccurredAtUtc).ToList());
    public Task AddAsync(SprintEvent sprintEvent, CancellationToken ct) { Items.Add(sprintEvent); return Task.CompletedTask; }
    public Task RemoveRangeAsync(IReadOnlyCollection<SprintEvent> events, CancellationToken ct)
    {
        foreach (var e in events) { Items.Remove(e); }
        return Task.CompletedTask;
    }
}

internal sealed class FakeChecklistRepository : IAgileChecklistRepository
{
    public List<AgileChecklistItem> Items { get; } = [];
    public Task<AgileChecklistItem?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(i => i.Id == id));
    public Task<IReadOnlyList<AgileChecklistItem>> ListByTaskAsync(Guid taskId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgileChecklistItem>>(Items.Where(i => i.TaskId == taskId).OrderBy(i => i.Order).ToList());
    public Task<IReadOnlyList<AgileChecklistItem>> ListByTasksAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgileChecklistItem>>(Items.Where(i => taskIds.Contains(i.TaskId)).ToList());
    public Task AddAsync(AgileChecklistItem item, CancellationToken ct) { Items.Add(item); return Task.CompletedTask; }
    public Task RemoveAsync(AgileChecklistItem item, CancellationToken ct) { Items.Remove(item); return Task.CompletedTask; }
    public Task RemoveRangeAsync(IReadOnlyCollection<AgileChecklistItem> items, CancellationToken ct)
    {
        foreach (var i in items) { Items.Remove(i); }
        return Task.CompletedTask;
    }
}

internal sealed class FakeAgileUnitOfWork : IAgileUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
}

/// <summary>A clock the test moves: events are stamped with it, so burn charts can be built day by day.</summary>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Set(DateOnly date, int hour = 12) => _now = new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
    public void AdvanceDays(int days) => _now = _now.AddDays(days);
}

/// <summary>All of the Agile services wired to in-memory fakes, sharing one clock.</summary>
internal sealed class AgileFixture
{
    public static readonly Guid Tenant = Guid.NewGuid();
    public Guid ProjectId { get; } = Guid.NewGuid();
    public FakeAgileTaskRepository Tasks { get; } = new();
    public FakeSprintRepository Sprints { get; } = new();
    public FakeSprintEventRepository Events { get; } = new();
    public FakeChecklistRepository Checklist { get; } = new();
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero));
    public AgileTaskService TaskService { get; }
    public SprintService SprintService { get; }
    public AgileBoardService Board { get; }
    public AgileChecklistService ChecklistService { get; }
    public SprintTracker Tracker { get; }

    public AgileFixture()
    {
        var unitOfWork = new FakeAgileUnitOfWork();
        Tracker = new SprintTracker(Sprints, Events, Clock);
        TaskService = new AgileTaskService(Tasks, unitOfWork, new NotConfiguredApprovalRequester(), Sprints, Checklist, Tracker);
        SprintService = new SprintService(Sprints, Events, Tasks, unitOfWork, Tracker);
        Board = new AgileBoardService(Tasks, Checklist, unitOfWork, Tracker);
        ChecklistService = new AgileChecklistService(Tasks, Checklist, unitOfWork);
    }

    public async Task<AgileTaskDto> AddTaskAsync(
        string title, int? points = null, int? sprint = null, AgileTaskStatus status = AgileTaskStatus.ToDo,
        AgileTaskPriority priority = AgileTaskPriority.Medium, Guid? responsible = null)
    {
        var created = await TaskService.CreateAsync(
            new CreateAgileTaskRequest(Tenant, ProjectId, title, null, responsible, null, null, priority, sprint, points), default);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Message : null);
        if (status == AgileTaskStatus.ToDo)
        {
            return created.Value!;
        }

        return (await TaskService.ChangeStatusAsync(created.Value!.Id, new ChangeAgileTaskStatusRequest(status), default)).Value!;
    }

    public async Task<SprintDto> AddSprintAsync(DateOnly? start = null, DateOnly? end = null, string? name = null)
    {
        var created = await SprintService.CreateAsync(new CreateSprintRequest(Tenant, ProjectId, name, null, start, end), default);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Message : null);
        return created.Value!;
    }

    public async Task<SprintDto> StartedSprintAsync(DateOnly start, DateOnly end)
    {
        var sprint = await AddSprintAsync(start, end);
        var started = await SprintService.StartAsync(sprint.Id, new StartSprintRequest(null, null), default);
        Assert.True(started.IsSuccess, started.IsFailure ? started.Error.Message : null);
        return started.Value!;
    }

    public AgileTask Stored(Guid id) => Tasks.Items.Single(t => t.Id == id);

    public IReadOnlyList<(SprintEventType Type, int Points, int Sprint)> EventsFor(Guid taskId) =>
        Events.Items.Where(e => e.TaskId == taskId).OrderBy(e => e.OccurredAtUtc)
            .Select(e => (e.Type, e.Points, e.SprintNumber)).ToList();
}
