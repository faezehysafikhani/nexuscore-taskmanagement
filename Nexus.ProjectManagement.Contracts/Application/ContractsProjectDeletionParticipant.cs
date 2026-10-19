using Nexus.ProjectManagement.Core.Application;

namespace Nexus.ProjectManagement.Contracts.Application;

/// <summary>Lets Project Core refuse to delete a project while this module still holds contracts or change requests for it.</summary>
public sealed class ContractsProjectDeletionParticipant(IContractRepository contracts, IChangeRequestRepository? changes = null) : IProjectDeletionParticipant
{
    public async Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var n = (await contracts.ListContractsAsync(projectId, cancellationToken)).Count;
        if (n > 0) found.Add($"{n} contract(s)");

        if (changes is not null)
        {
            var c = (await changes.ListAsync(projectId, cancellationToken)).Count;
            if (c > 0) found.Add($"{c} change request(s)");
        }

        return found.Count == 0 ? null : string.Join(", ", found);
    }
}
