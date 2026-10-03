using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>In-memory stand-ins for the Waterfall repositories, shared by the Waterfall tests.</summary>
internal sealed class FakeActivityRepository : IActivityRepository
{
    public List<Activity> Items { get; } = [];
    public Task<Activity?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(a => a.Id == id));
    public Task<IReadOnlyList<Activity>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Activity>>(Items.Where(a => a.ProjectId == projectId).OrderBy(a => a.Name).ToList());
    public Task AddAsync(Activity activity, CancellationToken ct) { Items.Add(activity); return Task.CompletedTask; }
    public Task RemoveAsync(Activity activity, CancellationToken ct) { Items.Remove(activity); return Task.CompletedTask; }
}

internal sealed class FakeDependencyRepository : IActivityDependencyRepository
{
    public List<ActivityDependency> Items { get; } = [];
    public Task<ActivityDependency?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(d => d.Id == id));
    public Task<IReadOnlyList<ActivityDependency>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ActivityDependency>>(Items.Where(d => d.ProjectId == projectId).ToList());
    public Task<IReadOnlyList<ActivityDependency>> ListByActivityAsync(Guid activityId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ActivityDependency>>(Items.Where(d => d.PredecessorActivityId == activityId || d.SuccessorActivityId == activityId).ToList());
    public Task AddAsync(ActivityDependency dependency, CancellationToken ct) { Items.Add(dependency); return Task.CompletedTask; }
    public Task RemoveAsync(ActivityDependency dependency, CancellationToken ct) { Items.Remove(dependency); return Task.CompletedTask; }
    public Task RemoveRangeAsync(IReadOnlyCollection<ActivityDependency> dependencies, CancellationToken ct)
    {
        foreach (var dependency in dependencies) { Items.Remove(dependency); }
        return Task.CompletedTask;
    }
}

internal sealed class FakeWaterfallUnitOfWork : IWaterfallUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
}

internal sealed class NotConfiguredApprovalRequester : IApprovalRequester
{
    public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) =>
        Task.FromResult(ApprovalRequestOutcome.NotConfigured);
}

internal sealed class FakeProjectRepository(params Nexus.ProjectManagement.Core.Domain.Project[] projects) : Nexus.ProjectManagement.Core.Application.IProjectRepository
{
    public Task<Nexus.ProjectManagement.Core.Domain.Project?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(projects.SingleOrDefault(p => p.Id == id));
    public Task<NexusCore.SharedKernel.Results.PagedResult<Nexus.ProjectManagement.Core.Domain.Project>> ListAsync(Nexus.ProjectManagement.Core.Application.Dtos.ListProjectsRequest request, CancellationToken ct) =>
        throw new NotSupportedException();
    public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => throw new NotSupportedException();
    public Task AddAsync(Nexus.ProjectManagement.Core.Domain.Project project, CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class FakeCalendarProvider(Nexus.ProjectManagement.Waterfall.Application.Scheduling.IWorkingDayCalendar? calendar)
    : Nexus.ProjectManagement.Waterfall.Application.Scheduling.IWorkingDayCalendarProvider
{
    public Guid? AskedForCalendar { get; private set; }
    public Guid? AskedForTenant { get; private set; }

    public Task<Nexus.ProjectManagement.Waterfall.Application.Scheduling.IWorkingDayCalendar?> GetAsync(Guid tenantId, Guid? calendarId, CancellationToken ct)
    {
        AskedForTenant = tenantId;
        AskedForCalendar = calendarId;
        return Task.FromResult(calendar);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class FakeBaselineRepository : IScheduleBaselineRepository
{
    public List<ScheduleBaseline> Baselines { get; } = [];
    public List<ScheduleBaselineActivity> Rows { get; } = [];
    public Task<ScheduleBaseline?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Baselines.SingleOrDefault(b => b.Id == id));
    public Task<IReadOnlyList<ScheduleBaseline>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ScheduleBaseline>>(Baselines.Where(b => b.ProjectId == projectId).OrderBy(b => b.Number).ToList());
    public Task<IReadOnlyList<ScheduleBaselineActivity>> ListActivitiesAsync(IReadOnlyCollection<Guid> baselineIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ScheduleBaselineActivity>>(Rows.Where(r => baselineIds.Contains(r.BaselineId)).ToList());
    public Task AddAsync(ScheduleBaseline baseline, IReadOnlyCollection<ScheduleBaselineActivity> activities, CancellationToken ct)
    {
        Baselines.Add(baseline);
        Rows.AddRange(activities);
        return Task.CompletedTask;
    }
    public Task RemoveAsync(ScheduleBaseline baseline, IReadOnlyCollection<ScheduleBaselineActivity> activities, CancellationToken ct)
    {
        Baselines.Remove(baseline);
        foreach (var row in activities) { Rows.Remove(row); }
        return Task.CompletedTask;
    }
}
