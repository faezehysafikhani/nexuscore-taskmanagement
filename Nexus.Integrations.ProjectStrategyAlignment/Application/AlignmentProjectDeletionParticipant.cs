using Nexus.ProjectManagement.Core.Application;

namespace Nexus.Integrations.StrategyAlignment.Application;

/// <summary>A project aligned to strategies cannot be deleted until the alignments are removed.</summary>
public sealed class AlignmentProjectDeletionParticipant(IAlignmentRepository alignments) : IProjectDeletionParticipant
{
    public async Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var count = (await alignments.ListAsync(tenantId, projectId, null, cancellationToken)).Count;
        return count > 0 ? $"{count} strategy alignment(s)" : null;
    }
}
