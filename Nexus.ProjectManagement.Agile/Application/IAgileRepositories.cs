using Nexus.ProjectManagement.Agile.Domain;

namespace Nexus.ProjectManagement.Agile.Application;

public interface ISprintRepository
{
    Task<Sprint?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Sprint?> GetByNumberAsync(Guid projectId, int number, CancellationToken cancellationToken);

    /// <summary>A project's sprints, by number.</summary>
    Task<IReadOnlyList<Sprint>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);

    Task AddAsync(Sprint sprint, CancellationToken cancellationToken);
    Task RemoveAsync(Sprint sprint, CancellationToken cancellationToken);
}

public interface ISprintEventRepository
{
    /// <summary>One sprint's events, oldest first.</summary>
    Task<IReadOnlyList<SprintEvent>> ListBySprintAsync(Guid projectId, int sprintNumber, CancellationToken cancellationToken);

    /// <summary>All of a project's sprint events, oldest first.</summary>
    Task<IReadOnlyList<SprintEvent>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);

    Task AddAsync(SprintEvent sprintEvent, CancellationToken cancellationToken);
    Task RemoveRangeAsync(IReadOnlyCollection<SprintEvent> events, CancellationToken cancellationToken);
}

public interface IAgileChecklistRepository
{
    Task<AgileChecklistItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgileChecklistItem>> ListByTaskAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>The items of several tasks at once (a board needs the counts for every card).</summary>
    Task<IReadOnlyList<AgileChecklistItem>> ListByTasksAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken);

    Task AddAsync(AgileChecklistItem item, CancellationToken cancellationToken);
    Task RemoveAsync(AgileChecklistItem item, CancellationToken cancellationToken);
    Task RemoveRangeAsync(IReadOnlyCollection<AgileChecklistItem> items, CancellationToken cancellationToken);
}
