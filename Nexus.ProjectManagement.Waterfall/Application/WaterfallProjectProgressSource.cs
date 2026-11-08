using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application;

/// <summary>Tells Core how far waterfall projects have got: the weighted roll-up of their activities' progress.</summary>
public sealed class WaterfallProjectProgressSource(IScheduleService scheduleService) : IProjectProgressSource
{
    public async Task<IReadOnlyList<ProjectProgressFigures>> GetProgressAsync(IReadOnlyCollection<Project> projects, CancellationToken cancellationToken)
    {
        var figures = new List<ProjectProgressFigures>();
        foreach (var project in projects.Where(p => p.Type == ProjectType.Waterfall))
        {
            var schedule = await scheduleService.GetScheduleAsync(project.Id, cancellationToken);
            if (schedule.IsSuccess && schedule.Value!.Activities.Count > 0)
            {
                figures.Add(new ProjectProgressFigures(project.Id, schedule.Value.PlannedProgress, schedule.Value.ActualProgress));
            }
        }

        return figures;
    }
}
