using Nexus.Actions.Application;
using Nexus.Actions.Domain;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Kpi.Application;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using Nexus.ProjectManagement.RiskManagement.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.Reporting.Analytics;

/// <summary>
/// The management boards: health, items needing attention, risks and KPIs of the running projects. Like the rest of the
/// analytics it only reads and repeats no module's rules - which projects count and how healthy each is comes from
/// <see cref="IAnalyticsService"/>, a risk's level from the Risk module, the KPI scale from <see cref="PerformanceRating"/>.
/// Risk management and KPIs are optional: a board that needs one that is not installed says so, and the attention board
/// simply has no items of that kind.
/// </summary>
public sealed class ManagementBoardService(
    IAnalyticsService analytics,
    IActionItemRepository actionRepository,
    IRiskService? riskService = null,
    IKpiRepository? kpiRepository = null,
    IKpiMeasurementRepository? measurementRepository = null,
    TimeProvider? timeProvider = null) : IManagementBoardService
{
    private const int MaxAttentionItems = 500;
    private const int TopRiskCount = 10;
    private const int TrendPoints = 12;

    private static readonly ActionStatus[] OpenActionStatuses = [ActionStatus.Open, ActionStatus.InProgress];

    private DateOnly Today => DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    private sealed record Scope(DateOnly AsOf, IReadOnlyList<ProjectPerformanceDto> Running);

    // ------------------------------------------------------------------ health

    public async Task<Result<HealthBoardDto>> GetHealthBoardAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var scope = await ScopeAsync(tenantId, asOf, cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure<HealthBoardDto>(scope.Error);
        }

        var rows = scope.Value!.Running
            .Select(p => new ProjectHealthRowDto(
                p.ProjectId, p.Name, p.Code, p.Status, p.OrganizationUnitId, p.ManagerUserId,
                p.Health.ToString(), p.PlannedProgress, p.ActualProgress, p.Spi, p.Cpi, p.ForecastDelayDays, p.IsOverdue))
            .OrderByDescending(r => Severity(r.Health))
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();

        var counts = new[] { HealthStatus.Green, HealthStatus.Amber, HealthStatus.Red, HealthStatus.Unknown }
            .Select(h => new CountByNameDto(h.ToString(), rows.Count(r => r.Health == h.ToString())))
            .ToList();

        return Result.Success(new HealthBoardDto(scope.Value.AsOf, rows.Count, counts, rows));
    }

    /// <summary>Red first, then Amber, then the ones nothing is known about, then Green.</summary>
    private static int Severity(string health) => health switch
    {
        nameof(HealthStatus.Red) => 3,
        nameof(HealthStatus.Amber) => 2,
        nameof(HealthStatus.Unknown) => 1,
        _ => 0
    };

    // --------------------------------------------------------------- attention

    public async Task<Result<AttentionBoardDto>> GetAttentionBoardAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var scope = await ScopeAsync(tenantId, asOf, cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure<AttentionBoardDto>(scope.Error);
        }

        var date = scope.Value!.AsOf;
        var running = scope.Value.Running;
        var names = running.ToDictionary(p => p.ProjectId, p => p.Name);
        var items = new List<AttentionItemDto>();

        foreach (var project in running)
        {
            if (project.IsOverdue && project.EndDate is { } end)
            {
                items.Add(new AttentionItemDto(
                    AttentionKinds.ProjectOverdue, AttentionSeverity.High, "Project", project.ProjectId, project.ProjectId, project.Name,
                    project.Name, end, date.DayNumber - end.DayNumber, project.ManagerUserId));
            }

            if (project.Health == HealthStatus.Red)
            {
                items.Add(new AttentionItemDto(
                    AttentionKinds.ProjectRed, AttentionSeverity.High, "Project", project.ProjectId, project.ProjectId, project.Name,
                    project.Name, null, null, project.ManagerUserId));
            }
            else if (project.Health == HealthStatus.Amber)
            {
                items.Add(new AttentionItemDto(
                    AttentionKinds.ProjectAtRisk, AttentionSeverity.Medium, "Project", project.ProjectId, project.ProjectId, project.Name,
                    project.Name, null, null, project.ManagerUserId));
            }
        }

        var risksIncluded = riskService is not null;
        if (risksIncluded)
        {
            var risks = await ApprovedRisksAsync(running, cancellationToken);
            if (risks.IsFailure)
            {
                return Result.Failure<AttentionBoardDto>(risks.Error);
            }

            foreach (var risk in risks.Value!.Where(r => IsOpenHigh(r) && string.IsNullOrWhiteSpace(r.ResponsePlan)))
            {
                items.Add(new AttentionItemDto(
                    AttentionKinds.RiskWithoutResponsePlan, AttentionSeverity.High, "Risk", risk.Id, risk.ProjectId, names.GetValueOrDefault(risk.ProjectId),
                    Truncate(risk.Description, 200), null, null, risk.RiskOwnerUserId));
            }
        }

        var runningIds = names.Keys.ToHashSet();
        var actions = await actionRepository.ListAsync(tenantId, projectId: null, cancellationToken);
        foreach (var action in actions.Where(a =>
                     OpenActionStatuses.Contains(a.Status) && a.EndDate is { } due && due < date && (a.ProjectId is null || runningIds.Contains(a.ProjectId.Value))))
        {
            var due = action.EndDate!.Value;
            items.Add(new AttentionItemDto(
                AttentionKinds.ActionOverdue, AttentionSeverity.Medium, "Action", action.Id, action.ProjectId,
                action.ProjectId is { } projectId ? names.GetValueOrDefault(projectId) : null,
                action.Title, due, date.DayNumber - due.DayNumber, action.ResponsibleUserId ?? action.OwnerUserId));
        }

        var ordered = items
            .OrderBy(i => i.Severity == AttentionSeverity.High ? 0 : 1)
            .ThenByDescending(i => i.DaysLate ?? int.MinValue)
            .ThenBy(i => i.Title, StringComparer.CurrentCulture)
            .ToList();

        var kinds = new[]
        {
            AttentionKinds.ProjectOverdue, AttentionKinds.ProjectRed, AttentionKinds.ProjectAtRisk,
            AttentionKinds.RiskWithoutResponsePlan, AttentionKinds.ActionOverdue
        };
        var counts = kinds.Select(k => new CountByNameDto(k, ordered.Count(i => i.Kind == k))).ToList();

        return Result.Success(new AttentionBoardDto(
            date, ordered.Count, ordered.Count > MaxAttentionItems, risksIncluded, counts, ordered.Take(MaxAttentionItems).ToList()));
    }

    // ------------------------------------------------------------------- risks

    public async Task<Result<RiskOverviewDto>> GetRiskOverviewAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken)
    {
        if (riskService is null)
        {
            return Result.Failure<RiskOverviewDto>(Error.Conflict("Risk management is not installed."));
        }

        var scope = await ScopeAsync(tenantId, asOf, cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure<RiskOverviewDto>(scope.Error);
        }

        var running = scope.Value!.Running;
        var risks = await ApprovedRisksAsync(running, cancellationToken);
        if (risks.IsFailure)
        {
            return Result.Failure<RiskOverviewDto>(risks.Error);
        }

        var all = risks.Value!;
        var byProject = all.GroupBy(r => r.ProjectId).ToDictionary(g => g.Key, g => g.ToList());
        var projects = running
            .Where(p => byProject.ContainsKey(p.ProjectId))
            .Select(p =>
            {
                var list = byProject[p.ProjectId];
                return new RiskOverviewProjectRowDto(
                    p.ProjectId, p.Name, p.Code, list.Count,
                    list.Count(r => LevelOf(r) == "High"), list.Count(r => LevelOf(r) == "Medium"), list.Count(r => LevelOf(r) == "Low"),
                    list.Count(IsOpenHigh));
            })
            .OrderByDescending(r => r.OpenHigh).ThenByDescending(r => r.High).ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();

        var names = running.ToDictionary(p => p.ProjectId, p => p.Name);
        var openHigh = all.Where(IsOpenHigh).ToList();
        var top = openHigh
            .OrderByDescending(r => r.Rpn).ThenBy(r => r.Description, StringComparer.CurrentCulture)
            .Take(TopRiskCount)
            .Select(r => new TopRiskDto(
                r.Id, r.ProjectId, names.GetValueOrDefault(r.ProjectId) ?? string.Empty, r.Description, r.ProbabilityScore, r.ImpactScore, r.Rpn,
                LevelOf(r), r.ResponseStatus.ToString(), r.RiskOwnerUserId, !string.IsNullOrWhiteSpace(r.ResponsePlan)))
            .ToList();

        var byStatus = Enum.GetValues<RiskResponseStatus>()
            .Select(s => new CountByNameDto(s.ToString(), all.Count(r => r.ResponseStatus == s)))
            .ToList();

        return Result.Success(new RiskOverviewDto(
            scope.Value.AsOf, all.Count,
            all.Count(r => LevelOf(r) == "High"), all.Count(r => LevelOf(r) == "Medium"), all.Count(r => LevelOf(r) == "Low"),
            openHigh.Count, openHigh.Count(r => string.IsNullOrWhiteSpace(r.ResponsePlan)),
            byStatus, projects, top));
    }

    /// <summary>Approved risks of the given projects; a draft or rejected risk is not yet an official one.</summary>
    private async Task<Result<IReadOnlyList<RiskDto>>> ApprovedRisksAsync(IReadOnlyList<ProjectPerformanceDto> projects, CancellationToken cancellationToken)
    {
        if (projects.Count == 0)
        {
            return Result.Success<IReadOnlyList<RiskDto>>([]);
        }

        var result = await riskService!.ListForProjectsAsync(new ListRisksForProjectsRequest(projects.Select(p => p.ProjectId).ToList()), cancellationToken);
        return result.IsFailure
            ? Result.Failure<IReadOnlyList<RiskDto>>(result.Error)
            : Result.Success<IReadOnlyList<RiskDto>>(result.Value!.Where(r => r.ApprovalStatus == ApprovalStatus.Approved).ToList());
    }

    /// <summary>The matrix colour the Risk module gave the risk (red, yellow, green) as a word: a project may recolour its matrix, but red stays the worst.</summary>
    private static string LevelOf(RiskDto risk) => risk.Level?.Trim().ToLowerInvariant() switch
    {
        "red" => "High",
        "green" => "Low",
        _ => "Medium"
    };

    private static bool IsOpenHigh(RiskDto risk) => LevelOf(risk) == "High" && risk.ResponseStatus != RiskResponseStatus.Completed;

    // -------------------------------------------------------------------- KPIs

    public async Task<Result<KpiOverviewDto>> GetKpiOverviewAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken)
    {
        if (kpiRepository is null || measurementRepository is null)
        {
            return Result.Failure<KpiOverviewDto>(Error.Conflict("KPI management is not installed."));
        }

        var scope = await ScopeAsync(tenantId, asOf, cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure<KpiOverviewDto>(scope.Error);
        }

        var date = scope.Value!.AsOf;
        var running = scope.Value.Running.ToDictionary(p => p.ProjectId);
        var statuses = new List<KpiStatusDto>();

        if (running.Count > 0)
        {
            var ids = running.Keys.ToList();
            var definitions = await kpiRepository.ListByProjectsAsync(ids, cancellationToken);
            var measurements = (await measurementRepository.ListByProjectsAsync(ids, cancellationToken))
                .Where(m => m.MeasuredOn <= date)
                .GroupBy(m => m.KpiDefinitionId)
                .ToDictionary(g => g.Key, g => g.OrderBy(m => m.MeasuredOn).ThenBy(m => m.CreatedAtUtc).ToList());

            foreach (var kpi in definitions)
            {
                var project = running[kpi.ProjectId];
                var values = measurements.GetValueOrDefault(kpi.Id) ?? [];
                var latest = values.Count > 0 ? values[^1] : null;
                decimal? achievement = latest is not null && kpi.TargetValue is > 0
                    ? Math.Round(latest.Value * 100m / kpi.TargetValue.Value, 2)
                    : null;

                statuses.Add(new KpiStatusDto(
                    kpi.Id, kpi.ProjectId, project.Name, project.Code, kpi.DeliverableId, kpi.Type.ToString(), kpi.Description, kpi.Formula,
                    kpi.TargetValue, latest?.Value, latest?.MeasuredOn, values.Count,
                    achievement, achievement is null ? null : PerformanceRating.Of(Math.Min(100m, achievement.Value)),
                    values.TakeLast(TrendPoints).Select(m => new KpiPointDto(m.MeasuredOn, m.Value)).ToList()));
            }
        }

        var ordered = statuses.OrderBy(k => k.ProjectCode, StringComparer.Ordinal).ThenBy(k => k.Description, StringComparer.CurrentCulture).ToList();
        var withAchievement = ordered.Where(k => k.AchievementPercent is not null).Select(k => Math.Min(100m, k.AchievementPercent!.Value)).ToList();

        return Result.Success(new KpiOverviewDto(
            date, ordered.Count, ordered.Count(k => k.MeasurementCount > 0), ordered.Count(k => k.MeasurementCount == 0),
            ordered.Count(k => k.Rating == PerformanceRating.Excellent), ordered.Count(k => k.Rating == PerformanceRating.Good),
            ordered.Count(k => k.Rating == PerformanceRating.NeedsImprovement),
            withAchievement.Count > 0 ? Math.Round(withAchievement.Average(), 2) : null, ordered));
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>The running projects (Active or OnHold) among those the analytics cover, with their performance as of the date.</summary>
    private async Task<Result<Scope>> ScopeAsync(Guid tenantId, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var date = asOf ?? Today;
        var all = await analytics.GetProjectsPerformanceAsync(tenantId, date, cancellationToken);
        if (all.IsFailure)
        {
            return Result.Failure<Scope>(all.Error);
        }

        var running = all.Value!
            .Where(p => p.Status == nameof(ProjectStatus.Active) || p.Status == nameof(ProjectStatus.OnHold))
            .ToList();
        return Result.Success(new Scope(date, running));
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}
