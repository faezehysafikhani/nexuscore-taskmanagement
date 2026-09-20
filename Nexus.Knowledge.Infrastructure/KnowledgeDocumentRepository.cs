using Microsoft.EntityFrameworkCore;
using Nexus.Knowledge.Application;
using Nexus.Knowledge.Domain;

namespace Nexus.Knowledge.Infrastructure;

public sealed class KnowledgeDocumentRepository(KnowledgeDbContext dbContext) : IKnowledgeDocumentRepository
{
    public Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.KnowledgeDocuments.SingleOrDefaultAsync(document => document.Id == id, cancellationToken);

    public async Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, CancellationToken cancellationToken)
    {
        var query = dbContext.KnowledgeDocuments.Where(document => document.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(document => document.Title.Contains(search) || (document.Description != null && document.Description.Contains(search)));
        }

        if (documentType is { } type)
        {
            query = query.Where(document => document.DocumentType == type);
        }

        return await query.OrderBy(document => document.Title).ToListAsync(cancellationToken);
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
}
