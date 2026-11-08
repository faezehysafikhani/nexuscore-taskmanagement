using Nexus.ProjectManagement.Agile.Domain;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Domain;

namespace Nexus.ProjectManagement.Agile.Application;

/// <summary>Tells Core how far agile projects have got: the share of their tasks that are done, with the task counts per column
/// (a task waiting for review counts as being worked on).</summary>
public sealed class AgileProjectProgressSource(IAgileTaskRepository repository) : IProjectProgressSource
{
    public async Task<IReadOnlyList<ProjectProgressFigures>> GetProgressAsync(IReadOnlyCollection<Project> projects, CancellationToken cancellationToken)
    {
        var figures = new List<ProjectProgressFigures>();
        foreach (var project in projects.Where(p => p.Type == ProjectType.Agile))
        {
            var tasks = await repository.ListByProjectAsync(project.Id, null, cancellationToken);
            var done = tasks.Count(t => t.Status == AgileTaskStatus.Done);
            var todo = tasks.Count(t => t.Status == AgileTaskStatus.ToDo);
            var actual = tasks.Count == 0 ? 0m : (decimal)done / tasks.Count * 100m;
            // Planned is left for Core to read from the project's dates: an agile project has no schedule of its own.
            figures.Add(new ProjectProgressFigures(project.Id, null, actual, tasks.Count, todo, tasks.Count - done - todo, done));
        }

        return figures;
    }
}
