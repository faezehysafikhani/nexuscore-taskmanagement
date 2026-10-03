using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Documents.Application;
using Nexus.ProjectManagement.Documents.Domain;

namespace Nexus.ProjectManagement.Documents.Infrastructure;

public sealed class ProjectDocumentRepository(ProjectDocumentsDbContext dbContext) : IProjectDocumentRepository
{
    public Task<ProjectDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProjectDocuments.SingleOrDefaultAsync(document => document.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProjectDocument>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ProjectDocuments
            .Where(document => document.ProjectId == projectId)
            .OrderByDescending(document => document.RegisterDate)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ProjectDocument document, CancellationToken cancellationToken)
    {
        await dbContext.ProjectDocuments.AddAsync(document, cancellationToken);
    }

    public Task RemoveAsync(ProjectDocument document, CancellationToken cancellationToken)
    {
        dbContext.ProjectDocuments.Remove(document);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ProjectDocumentVersion>> ListVersionsAsync(Guid documentId, CancellationToken cancellationToken) =>
        await dbContext.ProjectDocumentVersions
            .Where(version => version.DocumentId == documentId)
            .OrderBy(version => version.VersionNumber)
            .ToListAsync(cancellationToken);

    public async Task AddVersionAsync(ProjectDocumentVersion version, CancellationToken cancellationToken)
    {
        await dbContext.ProjectDocumentVersions.AddAsync(version, cancellationToken);
    }

    public Task RemoveVersionsAsync(IReadOnlyCollection<ProjectDocumentVersion> versions, CancellationToken cancellationToken)
    {
        dbContext.ProjectDocumentVersions.RemoveRange(versions);
        return Task.CompletedTask;
    }
}
