using Nexus.Knowledge.Domain;

namespace Nexus.Knowledge.Application;

public interface IKnowledgeDocumentRepository
{
    /// <summary>A document of the given organization only; another organization's id is simply not found.</summary>
    Task<KnowledgeDocument?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, CancellationToken cancellationToken);
    Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken);
    Task RemoveAsync(KnowledgeDocument document, CancellationToken cancellationToken);
}
