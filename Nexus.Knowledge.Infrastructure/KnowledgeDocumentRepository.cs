using Microsoft.EntityFrameworkCore;
using Nexus.Knowledge.Application;
using Nexus.Knowledge.Domain;

namespace Nexus.Knowledge.Infrastructure;

public sealed class KnowledgeDocumentRepository(
    KnowledgeDbContext dbContext) : IKnowledgeDocumentRepository
{
    public Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.KnowledgeDocuments.SingleOrDefaultAsync(document => document.Id == id, cancellationToken);

    public Task<KnowledgeDocument?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        dbContext.KnowledgeDocuments.SingleOrDefaultAsync(document =>
                document.Id == id &&
                document.TenantId == tenantId,
            cancellationToken);

    public Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, CancellationToken cancellationToken) =>
        SearchAsync(tenantId, search, documentType, null, null, cancellationToken);

    public async Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, string? subjectType, Guid? subjectId, CancellationToken cancellationToken)
    {
        var query = dbContext.KnowledgeDocuments.Where(document => document.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(document =>
                document.Title.Contains(search) ||
                (document.Description != null &&
                 document.Description.Contains(search)));
        }

        if (documentType is { } type)
        {
            query = query.Where(document => document.DocumentType == type);
        }

        if (!string.IsNullOrWhiteSpace(subjectType) && subjectId is { } relatedId)
        {
            var normalizedSubjectType = subjectType.Trim();

            query = query.Where(document =>
                dbContext.KnowledgeDocumentLinks.Any(link =>
                    link.DocumentId == document.Id &&
                    link.SubjectType == normalizedSubjectType &&
                    link.SubjectId == relatedId));
        }

        return await query.OrderBy(document => document.Title)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        await dbContext.KnowledgeDocuments.AddAsync(document, cancellationToken);
    }

    public Task RemoveAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        dbContext.KnowledgeDocuments.Remove(document);
        return Task.CompletedTask;
    }

    public Task<KnowledgeDocumentLink?> GetLinkAsync(Guid documentId, string subjectType, Guid subjectId, CancellationToken cancellationToken) =>
        dbContext.KnowledgeDocumentLinks.SingleOrDefaultAsync(link =>
                link.DocumentId == documentId &&
                link.SubjectType == subjectType &&
                link.SubjectId == subjectId,
            cancellationToken);

    public async Task AddLinkAsync(KnowledgeDocumentLink link, CancellationToken cancellationToken)
    {
        await dbContext.KnowledgeDocumentLinks.AddAsync(link, cancellationToken);
    }

    public Task RemoveLinkAsync(KnowledgeDocumentLink link, CancellationToken cancellationToken)
    {
        dbContext.KnowledgeDocumentLinks.Remove(link);
        return Task.CompletedTask;
    }
}