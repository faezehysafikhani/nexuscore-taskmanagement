using Nexus.ProjectManagement.Documents.Domain;

namespace Nexus.ProjectManagement.Documents.Application;

public interface IProjectDocumentRepository
{
    Task<ProjectDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectDocument>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task AddAsync(ProjectDocument document, CancellationToken cancellationToken);
    Task RemoveAsync(ProjectDocument document, CancellationToken cancellationToken);
}
