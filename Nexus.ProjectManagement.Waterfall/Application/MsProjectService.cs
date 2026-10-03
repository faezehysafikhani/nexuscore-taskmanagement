using System.Text;
using System.Text.RegularExpressions;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Application.MsProject;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public sealed class MsProjectService(
    IActivityRepository activityRepository,
    IActivityDependencyRepository dependencyRepository,
    IProjectRepository projectRepository,
    IScheduleService scheduleService,
    IWaterfallUnitOfWork unitOfWork) : IMsProjectService
{
    private const int MaxNameLength = 200;
    private const int MaxLagDays = 365;

    public async Task<Result<MsProjectImportResultDto>> ImportAsync(
        Guid tenantId, Guid projectId, Stream xml, bool replaceExisting, CancellationToken cancellationToken)
    {
        if (await projectRepository.GetByIdAsync(projectId, cancellationToken) is null)
        {
            return Result.Failure<MsProjectImportResultDto>(Error.NotFound("Project not found."));
        }

        var parsed = MsProjectXmlReader.Read(xml);
        if (parsed.IsFailure)
        {
            return Result.Failure<MsProjectImportResultDto>(parsed.Error);
        }

        var existing = await activityRepository.ListByProjectAsync(projectId, cancellationToken);
        if (existing.Count > 0 && !replaceExisting)
        {
            return Result.Failure<MsProjectImportResultDto>(Error.Conflict(
                "The project already has activities. Import with replaceExisting to delete them and use the file's plan instead."));
        }

        var plan = parsed.Value!;
        var warnings = new List<string>();
        var tree = BuildTree(plan, warnings);

        // Everything is validated and built before anything is deleted, so a bad file never costs the old plan.
        var built = BuildActivities(tenantId, projectId, plan, tree, warnings);
        var dependencies = BuildDependencies(tenantId, projectId, plan, tree, built, warnings);

        if (existing.Count > 0)
        {
            var oldLinks = await dependencyRepository.ListByProjectAsync(projectId, cancellationToken);
            await dependencyRepository.RemoveRangeAsync(oldLinks, cancellationToken);
            foreach (var activity in existing)
            {
                await activityRepository.RemoveAsync(activity, cancellationToken);
            }
        }

        foreach (var activity in built.Values)
        {
            await activityRepository.AddAsync(activity, cancellationToken);
        }

        foreach (var dependency in dependencies)
        {
            await dependencyRepository.AddAsync(dependency, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new MsProjectImportResultDto(built.Count, dependencies.Count, existing.Count, warnings));
    }

    public async Task<Result<MsProjectExportDto>> ExportAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return Result.Failure<MsProjectExportDto>(Error.NotFound("Project not found."));
        }

        var calculation = await scheduleService.CalculateAsync(projectId, cancellationToken);
        if (calculation.IsFailure)
        {
            return Result.Failure<MsProjectExportDto>(calculation.Error);
        }

        var links = (await dependencyRepository.ListByProjectAsync(projectId, cancellationToken))
            .Select(d => new ScheduleLinkInput(d.PredecessorActivityId, d.SuccessorActivityId, d.Type, d.LagDays)).ToList();

        var document = MsProjectXmlWriter.Write(project.Name, calculation.Value!.Result, links, calculation.Value.Calendar);
        var bytes = Encoding.UTF8.GetBytes(document.Declaration + Environment.NewLine + document.Root);

        var safeName = Regex.Replace(string.IsNullOrWhiteSpace(project.Code) ? project.Name : project.Code, @"[^\w\-\.]+", "_").Trim('_');
        return Result.Success(new MsProjectExportDto($"{(safeName.Length == 0 ? "project" : safeName)}.xml", bytes));
    }

    /// <summary>Each task's parent, from the outline levels: a task's parent is the nearest earlier
    /// task one level shallower. A level that skips ahead is treated as one level deeper.</summary>
    private static Dictionary<int, int?> BuildTree(MsProjectPlan plan, List<string> warnings)
    {
        var parentOf = new Dictionary<int, int?>();
        var stack = new List<(int Level, int Uid)>();
        var duplicates = 0;

        foreach (var task in plan.Tasks)
        {
            if (parentOf.ContainsKey(task.Uid))
            {
                duplicates++;
                continue;
            }

            var level = Math.Min(task.OutlineLevel, stack.Count + 1);
            stack.RemoveRange(level - 1, stack.Count - (level - 1));
            parentOf[task.Uid] = stack.Count > 0 ? stack[^1].Uid : null;
            stack.Add((level, task.Uid));
        }

        if (duplicates > 0)
        {
            warnings.Add($"{duplicates} task(s) repeated a unique ID already used and were skipped.");
        }

        return parentOf;
    }

    private static Dictionary<int, Activity> BuildActivities(
        Guid tenantId, Guid projectId, MsProjectPlan plan, Dictionary<int, int?> parentOf, List<string> warnings)
    {
        var built = new Dictionary<int, Activity>();
        var hasChildren = parentOf.Values.Where(p => p is not null).Select(p => p!.Value).ToHashSet();
        var unnamed = 0;
        var truncated = 0;

        foreach (var task in plan.Tasks.Where(t => parentOf.ContainsKey(t.Uid) && !built.ContainsKey(t.Uid)))
        {
            var name = task.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "(unnamed)";
                unnamed++;
            }
            else if (name.Length > MaxNameLength)
            {
                name = name[..MaxNameLength];
                truncated++;
            }

            var parent = parentOf[task.Uid] is { } parentUid ? built[parentUid].Id : (Guid?)null;
            var activity = new Activity(Guid.NewGuid(), tenantId, projectId, name, parent);
            var isLeaf = !hasChildren.Contains(task.Uid);

            int? days = task.DurationMinutes is { } minutes
                ? (minutes > 0 ? Math.Max((int)Math.Round(minutes / (double)plan.MinutesPerDay), 1) : 0)
                : null;
            activity.UpdateDetails(
                name, null, parent, null, null, null, task.Start, task.Finish,
                isLeaf ? days : null, null, 0);

            if (task.IsMilestone && isLeaf)
            {
                activity.SetMilestone(true);
            }

            if (isLeaf && task.PercentComplete > 0)
            {
                activity.UpdateProgress(0, task.PercentComplete);
            }

            built[task.Uid] = activity;
        }

        if (unnamed > 0)
        {
            warnings.Add($"{unnamed} task(s) had no name and were called \"(unnamed)\".");
        }

        if (truncated > 0)
        {
            warnings.Add($"{truncated} task name(s) were longer than {MaxNameLength} characters and were shortened.");
        }

        warnings.Add("Resources, assignments, costs, constraints and calendars are not imported. "
            + "Dates come from this system's own schedule calculation (apply the schedule to refresh them).");
        return built;
    }

    private static List<ActivityDependency> BuildDependencies(
        Guid tenantId, Guid projectId, MsProjectPlan plan, Dictionary<int, int?> parentOf,
        Dictionary<int, Activity> built, List<string> warnings)
    {
        var summaries = parentOf.Values.Where(p => p is not null).Select(p => p!.Value).ToHashSet();
        var accepted = new List<ActivityDependency>();
        var pairs = new HashSet<(Guid, Guid)>();
        int skippedSummary = 0, skippedUnknown = 0, skippedDuplicate = 0, skippedCycle = 0, clamped = 0;

        foreach (var task in plan.Tasks.Where(t => built.ContainsKey(t.Uid)))
        {
            foreach (var link in task.Links)
            {
                if (!built.TryGetValue(link.PredecessorUid, out var predecessor))
                {
                    skippedUnknown++;
                    continue;
                }

                var successor = built[task.Uid];
                if (summaries.Contains(task.Uid) || summaries.Contains(link.PredecessorUid))
                {
                    skippedSummary++;
                    continue;
                }

                if (!pairs.Add((predecessor.Id, successor.Id)))
                {
                    skippedDuplicate++;
                    continue;
                }

                if (DependencyGraph.WouldCreateCycle(accepted.Select(a => (a.PredecessorActivityId, a.SuccessorActivityId)), predecessor.Id, successor.Id))
                {
                    skippedCycle++;
                    continue;
                }

                var lag = (int)Math.Round(link.LagMinutes / (double)plan.MinutesPerDay);
                if (Math.Abs(lag) > MaxLagDays)
                {
                    lag = Math.Sign(lag) * MaxLagDays;
                    clamped++;
                }

                accepted.Add(new ActivityDependency(Guid.NewGuid(), tenantId, projectId, predecessor.Id, successor.Id, link.Type, lag));
            }
        }

        void Note(int count, string text)
        {
            if (count > 0)
            {
                warnings.Add($"{count} {text}");
            }
        }

        Note(skippedSummary, "link(s) to or from summary tasks were skipped (links connect tasks without sub-tasks).");
        Note(skippedUnknown, "link(s) referred to a task that is not in the file and were skipped.");
        Note(skippedDuplicate, "duplicate link(s) between the same two tasks were skipped.");
        Note(skippedCycle, "link(s) would have formed a circular dependency and were skipped.");
        Note(clamped, $"link lag(s) beyond {MaxLagDays} days were limited to {MaxLagDays}.");
        return accepted;
    }
}
