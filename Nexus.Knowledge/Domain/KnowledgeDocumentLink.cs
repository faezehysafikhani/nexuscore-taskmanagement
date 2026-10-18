using NexusCore.SharedKernel.Domain;

namespace Nexus.Knowledge.Domain;

/// <summary>
/// Links a knowledge document to an external subject without depending
/// on the module that owns that subject.
/// </summary>
public sealed class KnowledgeDocumentLink : Entity<Guid>
{
    private KnowledgeDocumentLink() : base(Guid.Empty)
    {
        SubjectType = string.Empty;
    }

    public KnowledgeDocumentLink(
        Guid id,
        Guid documentId,
        string subjectType,
        Guid subjectId) : base(id)
    {
        DocumentId = documentId;
        SubjectType = subjectType.Trim();
        SubjectId = subjectId;
    }

    public Guid DocumentId { get; private set; }

    public string SubjectType { get; private set; }

    public Guid SubjectId { get; private set; }
}