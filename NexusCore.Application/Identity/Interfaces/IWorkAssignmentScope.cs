namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Whom a user may make responsible for work (a task). The Core does not limit it: the default
/// lets anyone in the tenant be. A product's organization structure can limit it - Nexus.OrgStructure,
/// when turned on, keeps it to the user's own level and below. Only people newly made responsible
/// are checked: whoever a task already has stays.
/// </summary>
public interface IWorkAssignmentScope
{
    /// <summary>Who <paramref name="assignerUserId"/> may not make responsible, or null when anyone may be.</summary>
    Task<WorkAssignmentLimit?> GetLimitAsync(Guid tenantId, Guid assignerUserId, CancellationToken cancellationToken);
}

/// <param name="BlockedUserIds">The people the user may not make responsible.</param>
public sealed record WorkAssignmentLimit(IReadOnlySet<Guid> BlockedUserIds);

public sealed class UnlimitedWorkAssignmentScope : IWorkAssignmentScope
{
    public Task<WorkAssignmentLimit?> GetLimitAsync(Guid tenantId, Guid assignerUserId, CancellationToken cancellationToken) =>
        Task.FromResult<WorkAssignmentLimit?>(null);
}
