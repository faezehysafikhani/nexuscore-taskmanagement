using Nexus.ProjectManagement.Documents.Domain;

namespace Nexus.ProjectManagement.Documents.Application;

public interface IProjectDocumentRepository
{
    /// <summary>For system code that works from an event (approvals), which has no signed-in user.</summary>
    Task<ProjectDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>A document of the given organization only; another organization's id is simply not found.</summary>
    Task<ProjectDocument?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectDocument>> ListByProjectAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken);

    /// <summary>The items of several projects of the given organization at once. A repository written before this existed reports it as unsupported.</summary>
    Task<IReadOnlyList<ProjectDocument>> ListByProjectsAsync(Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This repository cannot list the items of several projects.");
    Task AddAsync(ProjectDocument document, CancellationToken cancellationToken);
    Task RemoveAsync(ProjectDocument document, CancellationToken cancellationToken);

    /// <summary>A document's persisted versions, oldest first. Empty for a document that
    /// predates version history and has not had a second file uploaded yet.</summary>
    Task<IReadOnlyList<ProjectDocumentVersion>> ListVersionsAsync(Guid documentId, CancellationToken cancellationToken);
    Task AddVersionAsync(ProjectDocumentVersion version, CancellationToken cancellationToken);
    Task RemoveVersionsAsync(IReadOnlyCollection<ProjectDocumentVersion> versions, CancellationToken cancellationToken);
}
