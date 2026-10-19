using Nexus.Knowledge.Domain;

namespace Nexus.Knowledge.Application;

public interface IKnowledgeDocumentRepository
{
    Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<KnowledgeDocument?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, string? subjectType, Guid? subjectId, CancellationToken cancellationToken);
    Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken);
    Task RemoveAsync(KnowledgeDocument document, CancellationToken cancellationToken);
    Task<KnowledgeDocumentLink?> GetLinkAsync(Guid documentId, string subjectType, Guid subjectId, CancellationToken cancellationToken);
    Task AddLinkAsync(KnowledgeDocumentLink link, CancellationToken cancellationToken);
    Task RemoveLinkAsync(KnowledgeDocumentLink link, CancellationToken cancellationToken);
}