using Nexus.ProjectManagement.Core.Application;

namespace Nexus.ProjectManagement.Documents.Application;

/// <summary>Lets Project Core refuse to delete a project while this module still holds documents for it.</summary>
public sealed class DocumentsProjectDeletionParticipant(IProjectDocumentRepository documents) : IProjectDeletionParticipant
{
    public async Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var n = (await documents.ListByProjectAsync(projectId, cancellationToken)).Count;
        if (n > 0) found.Add($"{n} document(s)");

        return found.Count == 0 ? null : string.Join(", ", found);
    }
}
