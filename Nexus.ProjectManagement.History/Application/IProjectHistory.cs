using Nexus.ProjectManagement.History.Domain;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.History.Application;

public interface IProjectHistoryUnitOfWork : IUnitOfWork;

/// <summary>Only changes of the caller's own tenant are ever returned (TenantId), including when a project id of another tenant is asked for.</summary>
public sealed record ProjectHistoryQuery(
    Guid TenantId, Guid ProjectId, string? EntityName = null, ProjectChangeKind? Kind = null, Guid? UserId = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, int Skip = 0, int Take = 50);

public interface IProjectHistoryRepository
{
    Task AddRangeAsync(IReadOnlyCollection<ProjectChange> changes, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<(IReadOnlyList<ProjectChange> Items, int Total)> QueryAsync(ProjectHistoryQuery query, CancellationToken cancellationToken);
}
