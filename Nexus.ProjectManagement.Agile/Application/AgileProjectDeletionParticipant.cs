using Nexus.ProjectManagement.Core.Application;

namespace Nexus.ProjectManagement.Agile.Application;

/// <summary>Lets Project Core refuse to delete a project while this module still holds agile tasks for it.</summary>
public sealed class AgileProjectDeletionParticipant(IAgileTaskRepository tasks) : IProjectDeletionParticipant
{
    public async Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var tasks_ = (await tasks.ListByProjectAsync(projectId, null, cancellationToken)).Count;
        if (tasks_ > 0) found.Add($"{tasks_} agile task(s)");

        return found.Count == 0 ? null : string.Join(", ", found);
    }
}
