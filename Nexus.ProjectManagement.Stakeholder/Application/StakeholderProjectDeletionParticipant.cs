using Nexus.ProjectManagement.Core.Application;

namespace Nexus.ProjectManagement.StakeholderManagement.Application;

/// <summary>Lets Project Core refuse to delete a project while this module still holds stakeholders for it.</summary>
public sealed class StakeholderProjectDeletionParticipant(IStakeholderRepository stakeholders) : IProjectDeletionParticipant
{
    public async Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var n = (await stakeholders.ListByProjectAsync(projectId, cancellationToken)).Count;
        if (n > 0) found.Add($"{n} stakeholder(s)");

        return found.Count == 0 ? null : string.Join(", ", found);
    }
}
