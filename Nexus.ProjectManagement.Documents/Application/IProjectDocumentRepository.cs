using Nexus.ProjectManagement.Documents.Domain;

namespace Nexus.ProjectManagement.Documents.Application;

public interface IProjectDocumentRepository
{
    Task<ProjectDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectDocument>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddAsync(ProjectDocument document, CancellationToken cancellationToken);
    Task RemoveAsync(ProjectDocument document, CancellationToken cancellationToken);

    /// <summary>A document's persisted versions, oldest first. Empty for a document that
    /// predates version history and has not had a second file uploaded yet.</summary>
    Task<IReadOnlyList<ProjectDocumentVersion>> ListVersionsAsync(Guid documentId, CancellationToken cancellationToken);
    Task AddVersionAsync(ProjectDocumentVersion version, CancellationToken cancellationToken);
    Task RemoveVersionsAsync(IReadOnlyCollection<ProjectDocumentVersion> versions, CancellationToken cancellationToken);
}
