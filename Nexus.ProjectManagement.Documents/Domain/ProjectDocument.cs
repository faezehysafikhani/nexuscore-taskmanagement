using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Documents.Domain;

public enum ProjectDocumentType { Report, Letter, MeetingMinutes, Other }

/// <summary>Owns only metadata; the file bytes live behind NexusCore's shared IFileStorage,
/// referenced here by StorageKey - Documents never implements its own storage.</summary>
public sealed class ProjectDocument : AuditableEntity<Guid>
{
    private ProjectDocument() : base(Guid.Empty)
    {
        Description = string.Empty;
        StorageKey = string.Empty;
        FileName = string.Empty;
        ContentType = string.Empty;
    }

    public ProjectDocument(
        Guid id, Guid tenantId, Guid projectId, string description, ProjectDocumentType documentType,
        string storageKey, string fileName, string contentType, long sizeBytes) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Description = description.Trim();
        DocumentType = documentType;
        RegisterDate = DateOnly.FromDateTime(DateTime.UtcNow);
        StorageKey = storageKey;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        ApprovalStatus = ApprovalStatus.NotSubmitted;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Description { get; private set; }
    public ProjectDocumentType DocumentType { get; private set; }
    public DateOnly RegisterDate { get; private set; }
    public string StorageKey { get; private set; }
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public ApprovalStatus ApprovalStatus { get; private set; }

    public void UpdateDescription(string description, ProjectDocumentType documentType)
    {
        Description = description.Trim();
        DocumentType = documentType;
    }

    public void MarkPendingApproval() => ApprovalStatus = ApprovalStatus.PendingApproval;

    public void Approve() => ApprovalStatus = ApprovalStatus.Approved;

    public void Reject() => ApprovalStatus = ApprovalStatus.Rejected;
}
