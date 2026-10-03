using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.Integrations.StrategyAlignment.Application;
using Nexus.Integrations.StrategyAlignment.Domain;
using Nexus.Organization.Application;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Domain;
using Nexus.StrategyManagement.Application;
using NexusCore.SharedKernel.Results;

namespace Nexus.Reporting.Application.Analytics;

/// <summary>
/// Read-only analytics over what the other modules already hold; it stores nothing and repeats none
/// of their rules (progress classification and contract money come from Progress and Contracts).
/// Every data source except Project and Actions is optional and arrives as a nullable constructor
/// parameter: a report that needs a missing one fails with a clear conflict, and the figures that do
/// not need it are simply left null. Archived projects are left out of every report.
/// </summary>
public sealed class AnalyticsService(
    IProjectRepository projectRepository,
    IActionItemRepository actionRepository,
    IProgressService? progressService = null,
    IContractService? contractService = null,
    IOrganizationUnitRepository? organizationRepository = null,
    IStrategyRepository? strategyRepository = null,
    IAlignmentRepository? alignmentRepository = null,
    TimeProvider? timeProvider = null) : IAnalyticsService
{
    private const int MaxProjects = 1000;
    private static readonly ProjectStatus[] Running = [ProjectStatus.Active, ProjectStatus.OnHold];
    private static readonly ActionStatus[] OpenActionStatuses = [ActionStatus.Open, ActionStatus.InProgress];

    /// <summary>A project with its latest progress update (null when Progress is not installed or nothing was reported).</summary>
    private sealed record Figure(Project Project, ProgressUpdateDto? Latest);

    private DateOnly Today => DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    // ------------------------------------------------------------ one project

    public async Task<Result<ProjectPerformanceDto>> GetProjectPerformanceAsync(Guid tenantId, Guid projectId, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.TenantId != tenantId)
        {
            return Result.Failure<ProjectPerformanceDto>(Error.NotFound("Project not found."));
        }

        var date = asOf ?? Today;
        var latest = await LatestProgressAsync(projectId, date, cancellationToken);

        decimal? contracted = null, invoiced = null, paid = null;
        if (contractService is not null)
        {
            var summary = await contractService.GetProjectSummaryAsync(projectId, cancellationToken);
            if (summary.IsSuccess && summary.Value!.ContractCount > 0)
            {
                contracted = summary.Value.TotalContractAmount.Rials;
                invoiced = summary.Value.TotalInvoiced.Rials;
                paid = summary.Value.TotalPaid.Rials;
            }
        }

        var ev = EarnedValueCalculator.Calculate(
            project.Cost, latest?.PlannedProgress, latest?.ActualProgress, invoiced, project.StartDate, project.EndDate, date);

        return Result.Success(new ProjectPerformanceDto(
            project.Id, project.Name, project.Code, project.Status.ToString(), project.OrganizationUnitId, project.ManagerUserId,
            project.StartDate, project.EndDate, date,
            latest?.PlannedProgress, latest?.ActualProgress, latest?.RegisterDate,
            ev.BudgetAtCompletion, ev.PlannedValue, ev.EarnedValueAmount, ev.ScheduleVariance, ev.Spi,
            ev.ActualCost, ev.CostVariance, ev.Cpi, ev.EstimateAtCompletion, ev.EstimateToComplete, ev.VarianceAtCompletion,
            ev.ForecastEnd, ev.ForecastDelayDays, ev.IsOverdue, ev.Health,
            contracted, invoiced, paid, contractService is not null, progressService is not null));
    }

    // ------------------------------------------------------------------ units

    public async Task<Result<UnitPerformanceReportDto>> GetUnitPerformanceAsync(Guid tenantId, int level, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var precondition = await UnitPreconditionAsync(tenantId, level, cancellationToken);
        if (precondition.Error is not null)
        {
            return Result.Failure<UnitPerformanceReportDto>(precondition.Error);
        }

        var rollup = precondition.Rollup!;
        var date = asOf ?? Today;
        var figures = await LoadFiguresAsync(tenantId, date, cancellationToken);

        var rows = figures
            .GroupBy(f => rollup.RowFor(f.Project.OrganizationUnitId, level))
            .ToDictionary(g => g.Key ?? Guid.Empty, g => g.ToList());

        var units = new List<UnitPerformanceDto>();
        foreach (var unit in rollup.Units.Where(u => rollup.DepthOf(u.Id) == level || rows.ContainsKey(u.Id)).OrderBy(u => u.Name, StringComparer.CurrentCulture))
        {
            units.Add(ToUnitRow(unit.Id, unit.Name, unit.Code, rollup.DepthOf(unit.Id), rows.GetValueOrDefault(unit.Id) ?? [], date));
        }

        if (rows.TryGetValue(Guid.Empty, out var unassigned))
        {
            units.Add(ToUnitRow(null, "Unassigned", null, 0, unassigned, date));
        }

        return Result.Success(new UnitPerformanceReportDto(level, rollup.MaxLevel, date, units));
    }

    public async Task<Result<UnitStatusMatrixDto>> GetUnitStatusMatrixAsync(Guid tenantId, int level, CancellationToken cancellationToken)
    {
        var precondition = await UnitPreconditionAsync(tenantId, level, cancellationToken);
        if (precondition.Error is not null)
        {
            return Result.Failure<UnitStatusMatrixDto>(precondition.Error);
        }

        var rollup = precondition.Rollup!;
        var projects = await LoadProjectsAsync(tenantId, cancellationToken);
        var statuses = Enum.GetValues<ProjectStatus>();
        var rows = projects.GroupBy(p => rollup.RowFor(p.OrganizationUnitId, level)).ToDictionary(g => g.Key ?? Guid.Empty, g => g.ToList());

        UnitStatusRowDto Row(Guid? id, string name, string? code)
        {
            var list = rows.GetValueOrDefault(id ?? Guid.Empty) ?? [];
            var counts = statuses.Select(s => list.Count(p => p.Status == s)).ToList();
            return new UnitStatusRowDto(id, name, code, counts, list.Count);
        }

        var result = rollup.Units
            .Where(u => rollup.DepthOf(u.Id) == level || rows.ContainsKey(u.Id))
            .OrderBy(u => u.Name, StringComparer.CurrentCulture)
            .Select(u => Row(u.Id, u.Name, u.Code))
            .ToList();
        if (rows.ContainsKey(Guid.Empty))
        {
            result.Add(Row(null, "Unassigned", null));
        }

        var columnTotals = statuses.Select((_, i) => result.Sum(r => r.Counts[i])).ToList();
        return Result.Success(new UnitStatusMatrixDto(level, statuses.Select(s => s.ToString()).ToList(), result, columnTotals, result.Sum(r => r.Total)));
    }

    // -------------------------------------------------------- project managers

    public async Task<Result<ProjectManagerEvaluationReportDto>> GetProjectManagerEvaluationAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var date = asOf ?? Today;
        var figures = await LoadFiguresAsync(tenantId, date, cancellationToken);
        var actions = await actionRepository.ListAsync(tenantId, projectId: null, cancellationToken);
        var openByPerson = actions
            .Where(a => OpenActionStatuses.Contains(a.Status) && a.ResponsibleUserId is not null)
            .GroupBy(a => a.ResponsibleUserId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var managers = figures
            .GroupBy(f => f.Project.ManagerUserId)
            .Select(g =>
            {
                var a = Aggregate(g.ToList(), date);
                return new ProjectManagerEvaluationDto(
                    g.Key, a.Count, a.Running, a.Completed, a.Overdue, a.Budget, a.WithProgress,
                    a.AvgPlanned, a.AvgActual, a.Deviation, a.OnTrack, a.AtRisk, a.Behind,
                    a.WithProgress > 0 ? Math.Round(a.OnTrack * 100m / a.WithProgress, 2) : null,
                    g.Key is { } id ? openByPerson.GetValueOrDefault(id) : 0);
            })
            .OrderByDescending(m => m.ProjectCount)
            .ThenBy(m => m.ManagerUserId)
            .ToList();

        return Result.Success(new ProjectManagerEvaluationReportDto(date, managers));
    }

    // ------------------------------------------------------ strategy alignment

    public async Task<Result<StrategyAlignmentMatrixDto>> GetStrategyAlignmentMatrixAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (strategyRepository is null || alignmentRepository is null)
        {
            return Result.Failure<StrategyAlignmentMatrixDto>(Error.Conflict("Strategy and project-strategy alignment are not installed."));
        }

        var strategies = (await strategyRepository.ListAsync(tenantId, cancellationToken)).OrderBy(s => s.Name, StringComparer.CurrentCulture).ToList();
        var alignments = await alignmentRepository.ListAsync(tenantId, null, null, cancellationToken);
        var projects = (await LoadProjectsAsync(tenantId, cancellationToken)).OrderBy(p => p.Code, StringComparer.Ordinal).ToList();

        var projectIds = projects.Select(p => p.Id).ToHashSet();
        var strategyIds = strategies.Select(s => s.Id).ToHashSet();
        // One cell per (project, strategy): links to a project or strategy that no longer exists are ignored.
        var cell = alignments
            .Where(a => projectIds.Contains(a.ProjectId) && strategyIds.Contains(a.StrategyId))
            .GroupBy(a => (a.ProjectId, a.StrategyId))
            .ToDictionary(g => g.Key, g => g.Last());

        var children = strategies.Where(s => s.ParentStrategyId is not null).GroupBy(s => s.ParentStrategyId!.Value).ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList());
        var byId = strategies.ToDictionary(s => s.Id);

        int DepthOf(Guid id)
        {
            var depth = 1;
            var seen = new HashSet<Guid> { id };
            while (byId[id].ParentStrategyId is { } parent && byId.ContainsKey(parent) && seen.Add(parent))
            {
                depth++;
                id = parent;
            }

            return depth;
        }

        HashSet<Guid> Subtree(Guid id)
        {
            var set = new HashSet<Guid> { id };
            var stack = new Stack<Guid>([id]);
            while (stack.Count > 0)
            {
                foreach (var child in children.GetValueOrDefault(stack.Pop()) ?? [])
                {
                    if (set.Add(child))
                    {
                        stack.Push(child);
                    }
                }
            }

            return set;
        }

        bool Aligned(Guid projectId, Guid strategyId) =>
            cell.TryGetValue((projectId, strategyId), out var a) && a.AlignmentLevel != AlignmentLevel.None;

        var columns = strategies.Select(s =>
        {
            var linked = projects.Where(p => Aligned(p.Id, s.Id)).ToList();
            var withPercent = linked.Select(p => cell[(p.Id, s.Id)].AlignmentPercentage).Where(v => v is not null).Select(v => v!.Value).ToList();
            var subtree = Subtree(s.Id);
            return new AlignmentStrategyColumnDto(
                s.Id, s.Name, s.ParentStrategyId, DepthOf(s.Id), s.Weight, linked.Count,
                projects.Count(p => subtree.Any(id => Aligned(p.Id, id))),
                linked.Count(p => cell[(p.Id, s.Id)].AlignmentLevel == AlignmentLevel.High),
                withPercent.Count > 0 ? Math.Round(withPercent.Average(), 2) : null);
        }).ToList();

        var rows = projects.Select(p =>
        {
            var cells = strategies.Select(s => cell.TryGetValue((p.Id, s.Id), out var a)
                ? new AlignmentCellDto(a.AlignmentLevel.ToString(), a.AlignmentPercentage)
                : new AlignmentCellDto(AlignmentLevel.None.ToString(), null)).ToList();
            var withPercent = strategies
                .Select(s => (s.Weight, Pct: cell.TryGetValue((p.Id, s.Id), out var a) && a.AlignmentLevel != AlignmentLevel.None ? a.AlignmentPercentage : null))
                .Where(x => x.Pct is not null)
                .ToList();
            var totalWeight = withPercent.Sum(x => x.Weight);
            return new AlignmentProjectRowDto(
                p.Id, p.Name, p.Code, cells, strategies.Count(s => Aligned(p.Id, s.Id)),
                withPercent.Count > 0 ? Math.Round(withPercent.Average(x => x.Pct!.Value), 2) : null,
                totalWeight > 0 ? Math.Round(withPercent.Sum(x => x.Weight * x.Pct!.Value) / totalWeight, 2) : null);
        }).ToList();

        return Result.Success(new StrategyAlignmentMatrixDto(
            columns, rows,
            columns.Where(c => c.DirectProjects == 0).Select(c => c.StrategyId).ToList(),
            rows.Where(r => r.AlignedStrategies == 0).Select(r => r.ProjectId).ToList()));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<(Error? Error, OrganizationRollup? Rollup)> UnitPreconditionAsync(Guid tenantId, int level, CancellationToken cancellationToken)
    {
        if (level < 1)
        {
            return (Error.Validation("The level must be 1 or more (1 = top-level units)."), null);
        }

        if (organizationRepository is null)
        {
            return (Error.Conflict("Organization is not installed."), null);
        }

        return (null, new OrganizationRollup(await organizationRepository.ListAsync(tenantId, cancellationToken)));
    }

    private async Task<IReadOnlyList<Project>> LoadProjectsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var page = await projectRepository.ListAsync(new ListProjectsRequest(tenantId, PageNumber: 1, PageSize: MaxProjects), cancellationToken);
        return page.Items.Where(p => p.Status != ProjectStatus.Archived).ToList();
    }

    private async Task<IReadOnlyList<Figure>> LoadFiguresAsync(Guid tenantId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var figures = new List<Figure>();
        foreach (var project in await LoadProjectsAsync(tenantId, cancellationToken))
        {
            figures.Add(new Figure(project, await LatestProgressAsync(project.Id, asOf, cancellationToken)));
        }

        return figures;
    }

    /// <summary>The most recent progress update registered on or before the date; none when Progress is not installed.</summary>
    private async Task<ProgressUpdateDto?> LatestProgressAsync(Guid projectId, DateOnly asOf, CancellationToken cancellationToken)
    {
        if (progressService is null)
        {
            return null;
        }

        var result = await progressService.ListByProjectAsync(projectId, cancellationToken);
        return result.IsSuccess
            ? result.Value!.Where(u => u.RegisterDate <= asOf).OrderByDescending(u => u.RegisterDate).FirstOrDefault()
            : null;
    }

    private static UnitPerformanceDto ToUnitRow(Guid? id, string name, string? code, int level, IReadOnlyList<Figure> figures, DateOnly asOf)
    {
        var a = Aggregate(figures, asOf);
        return new UnitPerformanceDto(
            id, name, code, level, a.Count, a.Running, a.Completed, a.Overdue, a.Budget, a.WithProgress,
            a.AvgPlanned, a.AvgActual, a.Deviation, a.OnTrack, a.AtRisk, a.Behind);
    }

    private sealed record Aggregates(
        int Count, int Running, int Completed, int Overdue, decimal Budget, int WithProgress,
        decimal? AvgPlanned, decimal? AvgActual, decimal? Deviation, int OnTrack, int AtRisk, int Behind);

    private static Aggregates Aggregate(IReadOnlyList<Figure> figures, DateOnly asOf)
    {
        var withProgress = figures.Where(f => f.Latest is not null).Select(f => f.Latest!).ToList();
        decimal? avgPlanned = withProgress.Count > 0 ? Math.Round(withProgress.Average(u => u.PlannedProgress), 2) : null;
        decimal? avgActual = withProgress.Count > 0 ? Math.Round(withProgress.Average(u => u.ActualProgress), 2) : null;
        return new Aggregates(
            figures.Count,
            figures.Count(f => Running.Contains(f.Project.Status)),
            figures.Count(f => f.Project.Status == ProjectStatus.Completed),
            figures.Count(f => Running.Contains(f.Project.Status) && f.Project.EndDate < asOf && f.Latest?.ActualProgress is null or < 100),
            figures.Sum(f => f.Project.Cost ?? 0m),
            withProgress.Count,
            avgPlanned, avgActual,
            avgPlanned is not null ? avgActual - avgPlanned : null,
            withProgress.Count(u => u.PerformanceClassification == PerformanceClassification.OnTrack),
            withProgress.Count(u => u.PerformanceClassification == PerformanceClassification.AtRisk),
            withProgress.Count(u => u.PerformanceClassification == PerformanceClassification.Behind));
    }
}
