using NexusCore.SharedKernel.Domain;

namespace Nexus.Knowledge.Domain;

public enum KnowledgeDocumentType { Book, Software, Notes, Other }

/// <summary>Standalone - no Project reference of any kind. Owns only metadata; file bytes
/// live behind NexusCore's shared IFileStorage, same reuse pattern as ProjectDocument.</summary>
public sealed class KnowledgeDocument : AuditableEntity<Guid>
{
    private KnowledgeDocument() : base(Guid.Empty)
    {
        Title = string.Empty;
        StorageKey = string.Empty;
        FileName = string.Empty;
        ContentType = string.Empty;
    }

    public KnowledgeDocument(
        Guid id, Guid tenantId, string title, KnowledgeDocumentType documentType,
        string storageKey, string fileName, string contentType, long sizeBytes) : base(id)
    {
        TenantId = tenantId;
        Title = title.Trim();
        DocumentType = documentType;
        StorageKey = storageKey;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
    }

    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public KnowledgeDocumentType DocumentType { get; private set; }
    public string StorageKey { get; private set; }
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long SizeBytes { get; private set; }

    public void UpdateDetails(string title, string? description, KnowledgeDocumentType documentType)
    {
        Title = title.Trim();
        Description = description;
        DocumentType = documentType;
    }
}
