using Nexus.ProjectManagement.Contracts.Application.Persian;
using Nexus.Reporting.Analytics;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Reporting.Export.Application;

public static class ReportKeys
{
    public const string UnitPerformance = "unit-performance";
    public const string UnitStatusMatrix = "unit-status-matrix";
    public const string ProjectManagers = "project-managers";
    public const string StrategyAlignment = "strategy-alignment";
    public const string Health = "health";
    public const string Attention = "attention";
    public const string Risks = "risks";
    public const string Kpis = "kpis";

    public static IReadOnlyList<string> All { get; } =
        [UnitPerformance, UnitStatusMatrix, ProjectManagers, StrategyAlignment, Health, Attention, Risks, Kpis];
}

/// <summary>Xlsx is a spreadsheet; Html is a print-ready page that the browser turns into a PDF.</summary>
public enum ExportFormat
{
    Xlsx,
    Html
}

/// <param name="Level">Organisation level for the unit reports (1 = top-level units); ignored by the others.</param>
public sealed record ExportRequest(string Report, ExportFormat Format, int Level = 2, DateOnly? AsOf = null);

public sealed record ExportFile(byte[] Content, string FileName, string ContentType);

public interface IReportExportService
{
    Task<Result<ExportFile>> ExportAsync(Guid tenantId, ExportRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Exports a report the analytics already provide: it asks the same service the JSON endpoint asks, so the file shows the very figures the
/// screen shows. The people's names come from the shared user directory when there is one.
/// </summary>
public sealed class ReportExportService(
    IAnalyticsService analytics,
    IManagementBoardService boards,
    IUserDirectory? users = null,
    TimeProvider? timeProvider = null) : IReportExportService
{
    private DateOnly Today => DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    public async Task<Result<ExportFile>> ExportAsync(Guid tenantId, ExportRequest request, CancellationToken cancellationToken)
    {
        var report = request.Report.Trim().ToLowerInvariant();
        var built = await BuildAsync(tenantId, report, request, cancellationToken);
        if (built.IsFailure)
        {
            return Result.Failure<ExportFile>(built.Error);
        }

        var date = (request.AsOf ?? Today).ToString("yyyy-MM-dd");
        var document = built.Value!;
        return request.Format == ExportFormat.Xlsx
            ? Result.Success(new ExportFile(XlsxRenderer.Render(document), $"{report}-{date}.xlsx", XlsxRenderer.ContentType))
            : Result.Success(new ExportFile(System.Text.Encoding.UTF8.GetBytes(HtmlRenderer.Render(document)), $"{report}-{date}.html", HtmlRenderer.ContentType));
    }

    private async Task<Result<ReportDocument>> BuildAsync(Guid tenantId, string report, ExportRequest request, CancellationToken cancellationToken)
    {
        var asOf = request.AsOf;
        switch (report)
        {
            case ReportKeys.UnitPerformance:
                return Convert(await analytics.GetUnitPerformanceAsync(tenantId, request.Level, asOf, cancellationToken), ReportBuilders.UnitPerformance);

            case ReportKeys.UnitStatusMatrix:
                return Convert(await analytics.GetUnitStatusMatrixAsync(tenantId, request.Level, cancellationToken), matrix => ReportBuilders.UnitStatusMatrix(matrix, asOf ?? Today));

            case ReportKeys.StrategyAlignment:
                return Convert(await analytics.GetStrategyAlignmentMatrixAsync(tenantId, cancellationToken), matrix => ReportBuilders.StrategyAlignment(matrix, asOf ?? Today));

            case ReportKeys.ProjectManagers:
            {
                var result = await analytics.GetProjectManagerEvaluationAsync(tenantId, asOf, cancellationToken);
                if (result.IsFailure)
                {
                    return Result.Failure<ReportDocument>(result.Error);
                }

                var names = await NamesAsync(result.Value!.Managers.Select(m => m.ManagerUserId), cancellationToken);
                return Result.Success(ReportBuilders.ProjectManagers(result.Value, names));
            }

            case ReportKeys.Health:
            {
                var result = await boards.GetHealthBoardAsync(tenantId, asOf, cancellationToken);
                if (result.IsFailure)
                {
                    return Result.Failure<ReportDocument>(result.Error);
                }

                var names = await NamesAsync(result.Value!.Projects.Select(p => p.ManagerUserId), cancellationToken);
                return Result.Success(ReportBuilders.Health(result.Value, names));
            }

            case ReportKeys.Attention:
            {
                var result = await boards.GetAttentionBoardAsync(tenantId, asOf, cancellationToken);
                if (result.IsFailure)
                {
                    return Result.Failure<ReportDocument>(result.Error);
                }

                var names = await NamesAsync(result.Value!.Items.Select(i => i.OwnerUserId), cancellationToken);
                return Result.Success(ReportBuilders.Attention(result.Value, names));
            }

            case ReportKeys.Risks:
            {
                var result = await boards.GetRiskOverviewAsync(tenantId, asOf, cancellationToken);
                if (result.IsFailure)
                {
                    return Result.Failure<ReportDocument>(result.Error);
                }

                var names = await NamesAsync(result.Value!.TopRisks.Select(r => r.RiskOwnerUserId), cancellationToken);
                return Result.Success(ReportBuilders.Risks(result.Value, names));
            }

            case ReportKeys.Kpis:
                return Convert(await boards.GetKpiOverviewAsync(tenantId, asOf, cancellationToken), ReportBuilders.Kpis);

            default:
                return Result.Failure<ReportDocument>(Error.Validation($"Unknown report '{report}'. Known reports: {string.Join(", ", ReportKeys.All)}."));
        }
    }

    private static Result<ReportDocument> Convert<T>(Result<T> result, Func<T, ReportDocument> build) =>
        result.IsFailure ? Result.Failure<ReportDocument>(result.Error) : Result.Success(build(result.Value!));

    private async Task<UserNames> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (users is null || wanted.Count == 0)
        {
            return UserNames.None;
        }

        var found = await users.GetUsersAsync(wanted, cancellationToken);
        return new UserNames(found.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().DisplayName));
    }
}
