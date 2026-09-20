using Nexus.ProjectManagement.Documents.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Documents.Application.Dtos;

public sealed record ProjectDocumentDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Description, ProjectDocumentType DocumentType,
    DateOnly RegisterDate, string FileName, string ContentType, long SizeBytes,
    ApprovalStatus ApprovalStatus, Guid? CreatedByUserId);

public sealed record UploadProjectDocumentRequest(
    Guid TenantId, Guid ProjectId, string Description, ProjectDocumentType DocumentType,
    string FileName, string ContentType);

public sealed record UpdateProjectDocumentRequest(string Description, ProjectDocumentType DocumentType);
