using Nexus.Knowledge.Application.Dtos;
using Nexus.Knowledge.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.Knowledge.Application;

public interface IKnowledgeDocumentService
{
    Task<Result<IReadOnlyList<KnowledgeDocumentDto>>> SearchAsync(Guid tenantId, string? search, KnowledgeDocumentType? documentType, CancellationToken cancellationToken);
    Task<Result<KnowledgeDocumentDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<KnowledgeDocumentDto>> UploadAsync(UploadKnowledgeDocumentRequest request, Stream content, CancellationToken cancellationToken);
    Task<Result<KnowledgeDocumentDto>> UpdateAsync(Guid id, UpdateKnowledgeDocumentRequest request, CancellationToken cancellationToken);
    Task<Result<(Stream Content, string FileName, string ContentType)>> DownloadAsync(Guid id, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
