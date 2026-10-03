using Nexus.ProjectManagement.Documents.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.Documents.Application.Dtos;

public sealed record ProjectDocumentDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Description, ProjectDocumentType DocumentType,
    DateOnly RegisterDate, string FileName, string ContentType, long SizeBytes,
    ApprovalStatus ApprovalStatus, Guid? CreatedByUserId, int CurrentVersion = 1);

public sealed record UploadProjectDocumentRequest(
    Guid TenantId, Guid ProjectId, string Description, ProjectDocumentType DocumentType,
    string FileName, string ContentType);

public sealed record UpdateProjectDocumentRequest(string Description, ProjectDocumentType DocumentType);

public sealed record ProjectDocumentVersionDto(
    Guid? Id, Guid DocumentId, int VersionNumber, string FileName, string ContentType, long SizeBytes,
    string? Comment, bool IsCurrent, Guid? CreatedByUserId, DateTimeOffset? CreatedAtUtc);

public sealed record UploadProjectDocumentVersionRequest(string FileName, string ContentType, string? Comment);
