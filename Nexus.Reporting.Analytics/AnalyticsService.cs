using Nexus.Actions.Application;
using NexusCore.Application.Approvals;
using Nexus.Actions.Domain;
using Nexus.Integrations.StrategyAlignment.Application;
using Nexus.Integrations.StrategyAlignment.Domain;
using Nexus.Organization.Application;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Domain;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.StrategyManagement.Application;
using NexusCore.SharedKernel.Results;

namespace Nexus.Reporting.Analytics;

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
    TimeProvider? timeProvider = null,
    IProjectProgressService? planProgressService = null,
    AnalyticsOptions? options = null,
    IProgressCurveService? progressCurveService = null) : IAnalyticsService
{
    private const int MaxProjects = 1000;
    private const int DefaultCurveStepDays = 14;
    private static readonly ProjectStatus[] Running = [ProjectStatus.Active, ProjectStatus.OnHold];
    private static readonly ActionStatus[] OpenActionStatuses = [ActionStatus.Open, ActionStatus.InProgress];

    /// <summary>Where a project stands: from its latest progress update, or (when asked for) from what its planning modules work out.</summary>
    private sealed record ProgressPoint(
        decimal PlannedProgress, decimal ActualProgress, decimal? Performance, DateOnly? RegisterDate, PerformanceClassification PerformanceClassification);

    /// <summary>A project with where it stands (null when nothing is known) and what its approved invoices come to (null without Contracts).</summary>
    private sealed record Figure(Project Project, ProgressPoint? Latest, decimal? ActualCost = null);

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
        var latest = await ProgressOfAsync(project, date, cancellationToken);
        return Result.Success(await PerformanceOfAsync(project, latest, date, cancellationToken));
    }

    /// <summary>The performance of every project the reports cover (archived ones are left out, and so are unapproved ones when
    /// <see cref="AnalyticsOptions.OnlyApprovedProjects"/> is on) - the same figures as the single-project report, in one read.</summary>
    public async Task<Result<IReadOnlyList<ProjectPerformanceDto>>> GetProjectsPerformanceAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var date = asOf ?? Today;
        var rows = new List<ProjectPerformanceDto>();
        foreach (var figure in await LoadFiguresAsync(tenantId, date, cancellationToken))
        {
            rows.Add(await PerformanceOfAsync(figure.Project, figure.Latest, date, cancellationToken));
        }

        return Result.Success<IReadOnlyList<ProjectPerformanceDto>>(rows);
    }

    private async Task<ProjectPerformanceDto> PerformanceOfAsync(Project project, ProgressPoint? latest, DateOnly date, CancellationToken cancellationToken)
    {
        decimal? contracted = null, invoiced = null, paid = null;
        if (contractService is not null)
        {
            var summary = await contractService.GetProjectSummaryAsync(project.Id, cancellationToken);
            if (summary.IsSuccess && summary.Value!.ContractCount > 0)
            {
                contracted = summary.Value.TotalContractAmount.Rials;
                invoiced = summary.Value.TotalInvoiced.Rials;
                paid = summary.Value.TotalPaid.Rials;
            }
        }

        var ev = EarnedValueCalculator.Calculate(
            project.Cost, latest?.PlannedProgress, latest?.ActualProgress, invoiced, project.StartDate, project.EndDate, date);

        return new ProjectPerformanceDto(
            project.Id, project.Name, project.Code, project.Status.ToString(), project.OrganizationUnitId, project.ManagerUserId,
            project.StartDate, project.EndDate, date,
            latest?.PlannedProgress, latest?.ActualProgress, latest?.RegisterDate,
            ev.BudgetAtCompletion, ev.PlannedValue, ev.EarnedValueAmount, ev.ScheduleVariance, ev.Spi,
            ev.ActualCost, ev.CostVariance, ev.Cpi, ev.EstimateAtCompletion, ev.EstimateToComplete, ev.VarianceAtCompletion,
            ev.ForecastEnd, ev.ForecastDelayDays, ev.IsOverdue, ev.Health,
            contracted, invoiced, paid, contractService is not null, progressService is not null || UsesPlan);
    }

    // ----------------------------------------------------------- cost curve

    public async Task<Result<CostCurveDto>> GetProjectCostCurveAsync(Guid tenantId, Guid projectId, int? stepDays, CancellationToken cancellationToken)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.TenantId != tenantId)
        {
            return Result.Failure<CostCurveDto>(Error.NotFound("Project not found."));
        }

        if (progressCurveService is null)
        {
            return Result.Failure<CostCurveDto>(Error.Conflict("The schedule (waterfall) module is not installed, so there is no planned progress to price."));
        }

        var curveResult = await progressCurveService.GetSCurveAsync(projectId, stepDays ?? DefaultCurveStepDays, cancellationToken);
        if (curveResult.IsFailure)
        {
            return Result.Failure<CostCurveDto>(curveResult.Error);
        }

        var curve = curveResult.Value!;
        var budget = project.Cost;
        decimal Price(decimal percent) => Math.Round(budget!.Value * percent / 100m, 2);

        var planned = budget is null ? [] : curve.Planned.Select(p => new MoneyPointDto(p.Date, Price(p.PlannedProgress))).ToList();
        var earned = budget is null ? [] : curve.Actual.Select(a => new MoneyPointDto(a.Date, Price(a.ActualProgress))).ToList();

        var invoices = await ApprovedInvoicesAsync(projectId, cancellationToken);
        var running = 0m;
        var actualCost = new List<MoneyPointDto>();
        foreach (var day in (invoices ?? []).GroupBy(i => i.Date).OrderBy(g => g.Key))
        {
            running += day.Sum(i => i.Amount);
            actualCost.Add(new MoneyPointDto(day.Key, running));
        }

        var costToday = (invoices ?? []).Where(i => i.Date <= curve.Today).Sum(i => i.Amount);
        return Result.Success(new CostCurveDto(
            project.Id, budget, curve.ProjectStart, curve.ProjectFinish, curve.Today, curve.StepDays,
            planned, earned, actualCost,
            budget is null ? null : Price(curve.CurrentPlannedProgress),
            budget is null ? null : Price(curve.CurrentActualProgress),
            costToday, invoices is not null));
    }

    /// <summary>The approved invoices of the project's contracts (date, amount in rials); null when Contracts is not installed.</summary>
    private async Task<List<(DateOnly Date, decimal Amount)>?> ApprovedInvoicesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (contractService is null)
        {
            return null;
        }

        var details = await contractService.ListForProjectsAsync(new ListContractsForProjectsRequest([projectId]), cancellationToken);
        if (!details.IsSuccess)
        {
            return null;
        }

        return details.Value!
            .SelectMany(d => d.Invoices)
            .Where(i => i.ApprovalStatus == ApprovalStatus.Approved)
            .Select(i => (i.InvoiceDate, i.Amount.Rials))
            .ToList();
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

        var all = Aggregate(figures, date);
        var summary = new PerformanceSummaryDto(all.Count, all.Running, all.Delayed, all.AvgPerformance, PerformanceRating.Of(all.AvgPerformance));
        return Result.Success(new UnitPerformanceReportDto(level, rollup.MaxLevel, date, units, summary));
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
            return new UnitStatusRowDto(id, name, code, counts, list.Count,
                statuses.Select(s => (IReadOnlyList<Guid>)list.Where(p => p.Status == s).Select(p => p.Id).ToList()).ToList());
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
                    g.Key is { } id ? openByPerson.GetValueOrDefault(id) : 0,
                    a.Delayed, a.AvgPerformance, PerformanceRating.Of(a.AvgPerformance), g.Select(f => f.Project.Id).ToList(),
                    a.ActualCost, a.EarnedValue, a.Cpi);
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
        return page.Items
            .Where(p => p.Status != ProjectStatus.Archived)
            .Where(p => options?.OnlyApprovedProjects != true || p.ApprovalStatus == ApprovalStatus.Approved)
            .ToList();
    }

    private async Task<IReadOnlyList<Figure>> LoadFiguresAsync(Guid tenantId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var projects = await LoadProjectsAsync(tenantId, cancellationToken);
        var invoiced = await InvoicedByProjectAsync(projects, cancellationToken);
        var figures = new List<Figure>();
        if (UsesPlan)
        {
            var known = await PlanFiguresAsync(tenantId, projects, cancellationToken);
            figures.AddRange(projects.Select(p => new Figure(p, known.GetValueOrDefault(p.Id), InvoicedOf(invoiced, p.Id))));
            return figures;
        }

        foreach (var project in projects)
        {
            figures.Add(new Figure(project, await LatestProgressAsync(project.Id, asOf, cancellationToken), InvoicedOf(invoiced, project.Id)));
        }

        return figures;
    }

    private static decimal? InvoicedOf(Dictionary<Guid, decimal>? invoiced, Guid projectId) =>
        invoiced is not null && invoiced.TryGetValue(projectId, out var amount) ? amount : null;

    /// <summary>What each project's approved invoices come to (rials), in one call; null when Contracts cannot say.
    /// A project without a contract has no entry, so its cost stays unknown rather than zero.</summary>
    private async Task<Dictionary<Guid, decimal>?> InvoicedByProjectAsync(IReadOnlyList<Project> projects, CancellationToken cancellationToken)
    {
        if (contractService is null || projects.Count == 0)
        {
            return null;
        }

        var details = await contractService.ListForProjectsAsync(new ListContractsForProjectsRequest(projects.Select(p => p.Id).ToList()), cancellationToken);
        if (!details.IsSuccess)
        {
            return null;
        }

        return details.Value!
            .GroupBy(d => d.Contract.ProjectId)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Contract.Summary.InvoicedAmount.Rials));
    }

    private bool UsesPlan => options?.ProgressFromProjectPlan == true && planProgressService is not null;

    private async Task<ProgressPoint?> ProgressOfAsync(Project project, DateOnly asOf, CancellationToken cancellationToken)
    {
        if (!UsesPlan)
        {
            return await LatestProgressAsync(project.Id, asOf, cancellationToken);
        }

        return (await PlanFiguresAsync(project.TenantId, [project], cancellationToken)).GetValueOrDefault(project.Id);
    }

    private async Task<Dictionary<Guid, ProgressPoint>> PlanFiguresAsync(Guid tenantId, IReadOnlyList<Project> projects, CancellationToken cancellationToken)
    {
        var known = new Dictionary<Guid, ProgressPoint>();
        if (projects.Count == 0)
        {
            return known;
        }

        var result = await planProgressService!.GetAsync(tenantId, new ProjectProgressRequest(projects.Select(p => p.Id).ToList()), cancellationToken);
        foreach (var figure in result.IsSuccess ? result.Value! : [])
        {
            known[figure.ProjectId] = new ProgressPoint(figure.PlannedProgress, figure.ActualProgress, figure.Performance, null, Classify(figure.ActualProgress - figure.PlannedProgress));
        }

        return known;
    }

    // The same bands the Progress module uses for a registered update.
    private static PerformanceClassification Classify(decimal deviation) => deviation switch
    {
        >= -5 => PerformanceClassification.OnTrack,
        >= -15 => PerformanceClassification.AtRisk,
        _ => PerformanceClassification.Behind
    };

    /// <summary>The most recent progress update registered on or before the date; none when Progress is not installed.</summary>
    private async Task<ProgressPoint?> LatestProgressAsync(Guid projectId, DateOnly asOf, CancellationToken cancellationToken)
    {
        if (progressService is null)
        {
            return null;
        }

        var result = await progressService.ListByProjectAsync(projectId, cancellationToken);
        var latest = result.IsSuccess
            ? result.Value!.Where(u => u.RegisterDate <= asOf).OrderByDescending(u => u.RegisterDate).FirstOrDefault()
            : null;
        return latest is null ? null : new ProgressPoint(latest.PlannedProgress, latest.ActualProgress, null, latest.RegisterDate, latest.PerformanceClassification);
    }

    private static UnitPerformanceDto ToUnitRow(Guid? id, string name, string? code, int level, IReadOnlyList<Figure> figures, DateOnly asOf)
    {
        var a = Aggregate(figures, asOf);
        return new UnitPerformanceDto(
            id, name, code, level, a.Count, a.Running, a.Completed, a.Overdue, a.Budget, a.WithProgress,
            a.AvgPlanned, a.AvgActual, a.Deviation, a.OnTrack, a.AtRisk, a.Behind,
            a.Delayed, a.AvgPerformance, PerformanceRating.Of(a.AvgPerformance), figures.Select(f => f.Project.Id).ToList(),
            a.ActualCost, a.EarnedValue, a.Cpi);
    }

    private sealed record Aggregates(
        int Count, int Running, int Completed, int Overdue, decimal Budget, int WithProgress,
        decimal? AvgPlanned, decimal? AvgActual, decimal? Deviation, int OnTrack, int AtRisk, int Behind,
        int Delayed, decimal? AvgPerformance, decimal? ActualCost = null, decimal? EarnedValue = null, decimal? Cpi = null);

    private static Aggregates Aggregate(IReadOnlyList<Figure> figures, DateOnly asOf)
    {
        var withProgress = figures.Where(f => f.Latest is not null).Select(f => f.Latest!).ToList();
        var costed = figures.Where(f => f.ActualCost is not null && f.Latest is not null && f.Project.Cost is not null).ToList();
        decimal? avgPlanned = withProgress.Count > 0 ? Math.Round(withProgress.Average(u => u.PlannedProgress), 2) : null;
        decimal? avgActual = withProgress.Count > 0 ? Math.Round(withProgress.Average(u => u.ActualProgress), 2) : null;
        var performances = withProgress.Where(u => u.Performance is not null).Select(u => u.Performance!.Value).ToList();
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
            withProgress.Count(u => u.PerformanceClassification == PerformanceClassification.Behind),
            withProgress.Count(u => u.PerformanceClassification != PerformanceClassification.OnTrack),
            performances.Count > 0 ? Math.Round(performances.Average(v => Math.Min(100m, v)), 2) : null,
            costed.Count > 0 ? costed.Sum(f => f.ActualCost!.Value) : null,
            costed.Count > 0 ? Math.Round(costed.Sum(f => f.Project.Cost!.Value * f.Latest!.ActualProgress / 100m), 2) : null,
            Cpi(costed));
    }

    /// <summary>Earned value over actual cost across the projects that have a budget, a known progress and invoices; null when it cannot be worked out.</summary>
    private static decimal? Cpi(IReadOnlyList<Figure> costed)
    {
        var cost = costed.Sum(f => f.ActualCost!.Value);
        var earned = costed.Sum(f => f.Project.Cost!.Value * f.Latest!.ActualProgress / 100m);
        return cost > 0 && earned > 0 ? Math.Round(earned / cost, 2) : null;
    }
}
