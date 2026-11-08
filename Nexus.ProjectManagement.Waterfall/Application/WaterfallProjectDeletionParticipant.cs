using Nexus.ProjectManagement.Core.Application;

namespace Nexus.ProjectManagement.Waterfall.Application;

/// <summary>Lets Project Core refuse to delete a project while this module still holds schedule data for it.</summary>
public sealed class WaterfallProjectDeletionParticipant(IActivityRepository activities, IScheduleBaselineRepository baselines, IProgressSnapshotRepository snapshots) : IProjectDeletionParticipant
{
    public async Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var a = (await activities.ListByProjectAsync(projectId, cancellationToken)).Count;
        if (a > 0) found.Add($"{a} activity(ies)");
        var b = (await baselines.ListByProjectAsync(projectId, cancellationToken)).Count;
        if (b > 0) found.Add($"{b} schedule baseline(s)");
        var c = (await snapshots.ListByProjectAsync(projectId, cancellationToken)).Count;
        if (c > 0) found.Add($"{c} progress snapshot(s)");

        return found.Count == 0 ? null : string.Join(", ", found);
    }
}
