using Nexus.ProjectManagement.Core.Application;

namespace Nexus.ProjectManagement.Contracts.Application;

/// <summary>Lets Project Core refuse to delete a project while this module still holds contracts for it.</summary>
public sealed class ContractsProjectDeletionParticipant(IContractRepository contracts) : IProjectDeletionParticipant
{
    public async Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var n = (await contracts.ListContractsAsync(projectId, cancellationToken)).Count;
        if (n > 0) found.Add($"{n} contract(s)");

        return found.Count == 0 ? null : string.Join(", ", found);
    }
}
