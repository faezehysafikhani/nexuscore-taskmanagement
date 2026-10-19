using Nexus.Knowledge.Domain;

namespace Nexus.Knowledge.Application.Dtos;

public sealed record KnowledgeDocumentDto(
    Guid Id, Guid TenantId, string Title, string? Description, KnowledgeDocumentType DocumentType,
    string FileName, string ContentType, long SizeBytes, DateTimeOffset CreatedAtUtc);

public sealed record UploadKnowledgeDocumentRequest(Guid TenantId, string Title, string? Description, KnowledgeDocumentType DocumentType, string FileName, string ContentType);

public sealed record UpdateKnowledgeDocumentRequest(string Title, string? Description, KnowledgeDocumentType DocumentType);

public sealed record CreateKnowledgeDocumentLinkRequest(string SubjectType, Guid SubjectId);

public sealed record KnowledgeDocumentLinkDto(Guid Id, Guid DocumentId, string SubjectType, Guid SubjectId);