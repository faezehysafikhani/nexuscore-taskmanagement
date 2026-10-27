using Nexus.ProjectManagement.Documents.Domain;

namespace Nexus.ProjectManagement.Documents.Application;

public interface IProjectDocumentRepository
{
    /// <summary>For system code that works from an event (approvals), which has no signed-in user.</summary>
    Task<ProjectDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>A document of the given organization only; another organization's id is simply not found.</summary>
    Task<ProjectDocument?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectDocument>> ListByProjectAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken);
    Task AddAsync(ProjectDocument document, CancellationToken cancellationToken);
    Task RemoveAsync(ProjectDocument document, CancellationToken cancellationToken);
}
