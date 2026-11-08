using Nexus.ProjectManagement.Core.Application;

namespace Nexus.ProjectManagement.Progress.Application;

/// <summary>Lets Project Core refuse to delete a project while this module still holds progress reports for it.</summary>
public sealed class ProgressProjectDeletionParticipant(IProgressRepository updates, IDelayReasonRepository delayReasons) : IProjectDeletionParticipant
{
    public async Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var a = (await updates.ListByProjectAsync(projectId, cancellationToken)).Count;
        if (a > 0) found.Add($"{a} status report(s)");
        var b = (await delayReasons.ListByProjectAsync(projectId, cancellationToken)).Count;
        if (b > 0) found.Add($"{b} delay reason(s)");

        return found.Count == 0 ? null : string.Join(", ", found);
    }
}
