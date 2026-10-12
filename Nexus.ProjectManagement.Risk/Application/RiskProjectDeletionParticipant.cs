using Nexus.ProjectManagement.Core.Application;

namespace Nexus.ProjectManagement.RiskManagement.Application;

/// <summary>Lets Project Core refuse to delete a project while this module still holds risks for it.</summary>
public sealed class RiskProjectDeletionParticipant(IRiskRepository risks) : IProjectDeletionParticipant
{
    public async Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var n = (await risks.ListByProjectAsync(projectId, cancellationToken)).Count;
        if (n > 0) found.Add($"{n} risk(s)");

        return found.Count == 0 ? null : string.Join(", ", found);
    }
}
