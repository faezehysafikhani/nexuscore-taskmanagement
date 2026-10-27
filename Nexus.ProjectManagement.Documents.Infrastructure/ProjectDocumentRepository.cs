using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Documents.Application;
using Nexus.ProjectManagement.Documents.Domain;

namespace Nexus.ProjectManagement.Documents.Infrastructure;

public sealed class ProjectDocumentRepository(ProjectDocumentsDbContext dbContext) : IProjectDocumentRepository
{
    public Task<ProjectDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ProjectDocuments.SingleOrDefaultAsync(document => document.Id == id, cancellationToken);

    public Task<ProjectDocument?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        dbContext.ProjectDocuments.SingleOrDefaultAsync(document => document.Id == id && document.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<ProjectDocument>> ListByProjectAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ProjectDocuments
            .Where(document => document.TenantId == tenantId && document.ProjectId == projectId)
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
}
