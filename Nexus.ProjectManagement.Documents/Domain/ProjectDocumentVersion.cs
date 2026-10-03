using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Documents.Domain;

/// <summary>
/// One uploaded file of a <see cref="ProjectDocument"/>. Versions are numbered from 1 and never
/// renumbered; the highest number is the document's current file. Like the document itself, a
/// version owns only metadata - the bytes live behind NexusCore's shared IFileStorage under
/// StorageKey, and each version has its own key, so uploading a new version never overwrites an
/// older file.
/// </summary>
public sealed class ProjectDocumentVersion : AuditableEntity<Guid>
{
    private ProjectDocumentVersion() : base(Guid.Empty)
    {
        StorageKey = string.Empty;
        FileName = string.Empty;
        ContentType = string.Empty;
    }

    public ProjectDocumentVersion(
        Guid id, Guid tenantId, Guid documentId, int versionNumber,
        string storageKey, string fileName, string contentType, long sizeBytes, string? comment = null) : base(id)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        VersionNumber = versionNumber;
        StorageKey = storageKey;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    public Guid TenantId { get; private set; }
    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string StorageKey { get; private set; }
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long SizeBytes { get; private set; }

    /// <summary>What changed in this version, as the uploader described it.</summary>
    public string? Comment { get; private set; }
}
