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
