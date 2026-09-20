using Nexus.Knowledge.Domain;

namespace Nexus.Knowledge.Application;

public interface IKnowledgeDocumentRepository
{
    Task<KnowledgeDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, CancellationToken cancellationToken);
    Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken);
    Task RemoveAsync(KnowledgeDocument document, CancellationToken cancellationToken);
}
