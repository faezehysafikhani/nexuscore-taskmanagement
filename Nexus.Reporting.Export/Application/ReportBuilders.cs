using Nexus.ProjectManagement.Contracts.Application.Persian;
using Nexus.Reporting.Analytics;

namespace Nexus.Reporting.Export.Application;

/// <summary>Display names for the people a report mentions; someone unknown shows as empty.</summary>
public sealed class UserNames(IReadOnlyDictionary<Guid, string> names)
{
    public static UserNames None { get; } = new(new Dictionary<Guid, string>());

    public string Of(Guid? id) => id is { } value && names.TryGetValue(value, out var name) ? name : string.Empty;
}

/// <summary>
/// Turns each analytics report into a <see cref="ReportDocument"/>: Persian headings and words, the numbers exactly as the
/// analytics worked them out. Nothing is recomputed here, so a file always agrees with the screen it was exported from.
/// </summary>
public static class ReportBuilders
{
    private static readonly object?[] NoRow = [];

    private static ReportColumn Text(string header) => new(header);
    private static ReportColumn Number(string header) => new(header, ColumnKind.Number);
    private static ReportColumn Date(string header) => new(header, ColumnKind.Date);

    private static ReportSection Section(string? title, ReportColumn[] columns, IEnumerable<object?[]> rows) =>
        new(title, columns, rows.Select(row => (IReadOnlyList<object?>)row).ToList());

    private static string Subtitle(DateOnly asOf) => $"وضعیت تا تاریخ {PersianDates.Format(asOf)}";

    private static string YesNo(bool value) => value ? "بله" : "خیر";

    // --------------------------------------------------------------- units

    public static ReportDocument UnitPerformance(UnitPerformanceReportDto report)
    {
        var sections = new List<ReportSection>
        {
            Section("واحدها",
                [Text("واحد"), Text("کد"), Number("تعداد پروژه"), Number("در حال اجرا"), Number("تکمیل‌شده"), Number("عقب‌افتاده از موعد"),
                 Number("بودجه کل"), Number("میانگین پیشرفت برنامه‌ریزی‌شده (٪)"), Number("میانگین پیشرفت واقعی (٪)"), Number("انحراف (٪)"),
                 Number("روی برنامه"), Number("در معرض ریسک"), Number("عقب‌مانده"), Number("پروژه‌های دارای تأخیر"),
                 Number("میانگین عملکرد (٪)"), Text("ارزیابی")],
                report.Units.Select(u => new object?[]
                {
                    u.UnitId is null ? PersianLabels.Unassigned : u.UnitName, u.UnitCode, u.ProjectCount, u.RunningProjects, u.CompletedProjects,
                    u.OverdueProjects, u.TotalBudget, u.AveragePlannedProgress, u.AverageActualProgress, u.AverageDeviation,
                    u.OnTrack, u.AtRisk, u.Behind, u.DelayedProjects, u.AveragePerformance, PersianLabels.Rating(u.Rating)
                }))
        };

        if (report.Summary is { } summary)
        {
            sections.Add(Section("جمع کل سازمان",
                [Number("تعداد پروژه"), Number("در حال اجرا"), Number("پروژه‌های دارای تأخیر"), Number("میانگین عملکرد (٪)"), Text("ارزیابی")],
                [[summary.ProjectCount, summary.RunningProjects, summary.DelayedProjects, summary.AveragePerformance, PersianLabels.Rating(summary.Rating)]]));
        }

        return new ReportDocument("عملکرد واحدهای سازمانی", Subtitle(report.AsOf), sections);
    }

    public static ReportDocument UnitStatusMatrix(UnitStatusMatrixDto matrix, DateOnly asOf)
    {
        var columns = new List<ReportColumn> { Text("واحد"), Text("کد") };
        columns.AddRange(matrix.Statuses.Select(s => Number(PersianLabels.ProjectStatus(s))));
        columns.Add(Number(PersianLabels.Total));

        var rows = matrix.Rows
            .Select(r => new List<object?> { r.UnitId is null ? PersianLabels.Unassigned : r.UnitName, r.UnitCode }
                .Concat(r.Counts.Cast<object?>()).Append(r.Total).ToArray())
            .ToList();
        rows.Add(new List<object?> { PersianLabels.Total, null }.Concat(matrix.ColumnTotals.Cast<object?>()).Append(matrix.Total).ToArray());

        return new ReportDocument("ماتریس وضعیت پروژه‌ها به تفکیک واحد", Subtitle(asOf), [Section(null, columns.ToArray(), rows)]);
    }

    // ------------------------------------------------------------ managers

    public static ReportDocument ProjectManagers(ProjectManagerEvaluationReportDto report, UserNames names) =>
        new("ارزیابی مدیران پروژه", Subtitle(report.AsOf),
        [
            Section(null,
                [Text("مدیر پروژه"), Number("تعداد پروژه"), Number("در حال اجرا"), Number("تکمیل‌شده"), Number("عقب‌افتاده از موعد"), Number("بودجه کل"),
                 Number("میانگین پیشرفت برنامه‌ریزی‌شده (٪)"), Number("میانگین پیشرفت واقعی (٪)"), Number("انحراف (٪)"),
                 Number("روی برنامه"), Number("در معرض ریسک"), Number("عقب‌مانده"), Number("سهم پروژه‌های روی برنامه (٪)"),
                 Number("اقدامات باز"), Number("پروژه‌های دارای تأخیر"), Number("میانگین عملکرد (٪)"), Text("ارزیابی")],
                report.Managers.Select(m => new object?[]
                {
                    m.ManagerUserId is null ? PersianLabels.Unassigned : names.Of(m.ManagerUserId), m.ProjectCount, m.RunningProjects, m.CompletedProjects,
                    m.OverdueProjects, m.TotalBudget, m.AveragePlannedProgress, m.AverageActualProgress, m.AverageDeviation,
                    m.OnTrack, m.AtRisk, m.Behind, m.OnTrackShare, m.OpenActions, m.DelayedProjects, m.AveragePerformance, PersianLabels.Rating(m.Rating)
                }))
        ]);

    // ----------------------------------------------------------- strategies

    public static ReportDocument StrategyAlignment(StrategyAlignmentMatrixDto matrix, DateOnly asOf)
    {
        var strategyNames = matrix.Strategies.ToDictionary(s => s.StrategyId, s => s.Name);
        var projectNames = matrix.Projects.ToDictionary(p => p.ProjectId, p => $"{p.Code} - {p.Name}");

        var cells = new List<ReportColumn> { Text("کد پروژه"), Text("نام پروژه") };
        cells.AddRange(matrix.Strategies.Select(s => Text(s.Name)));
        cells.AddRange([Number("تعداد راهبردهای همسو"), Number("میانگین (٪)"), Number("میانگین وزنی (٪)")]);

        var gaps = matrix.UncoveredStrategyIds.Select(id => new object?[] { "راهبرد بدون پروژه", strategyNames.GetValueOrDefault(id) })
            .Concat(matrix.UnalignedProjectIds.Select(id => new object?[] { "پروژه بدون راهبرد", projectNames.GetValueOrDefault(id) }))
            .ToList();

        var sections = new List<ReportSection>
        {
            Section("راهبردها",
                [Text("راهبرد"), Number("عمق"), Number("وزن"), Number("پروژه‌های مستقیم"), Number("پروژه‌ها با زیرمجموعه"), Number("همسویی زیاد"), Number("میانگین (٪)")],
                matrix.Strategies.Select(s => new object?[] { s.Name, s.Depth, s.Weight, s.DirectProjects, s.ProjectsIncludingChildren, s.HighAlignedProjects, s.AveragePercentage })),
            Section("ماتریس پروژه در راهبرد", cells.ToArray(),
                matrix.Projects.Select(p => new List<object?> { p.Code, p.Name }
                    .Concat(p.Cells.Select(c => (object?)(c.Percentage is { } percent ? percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : PersianLabels.AlignmentLevel(c.Level))))
                    .Concat([p.AlignedStrategies, p.AveragePercentage, p.WeightedPercentage]).ToArray()))
        };

        if (gaps.Count > 0)
        {
            sections.Add(Section("شکاف‌ها", [Text("نوع"), Text("نام")], gaps));
        }

        return new ReportDocument("ماتریس همسویی پروژه‌ها با راهبردها", Subtitle(asOf), sections);
    }

    // -------------------------------------------------------------- boards

    public static ReportDocument Health(HealthBoardDto board, UserNames names) =>
        new("وضعیت سلامت پروژه‌ها", Subtitle(board.AsOf),
        [
            Section("خلاصه", [Text("وضعیت سلامت"), Number("تعداد پروژه")],
                board.Counts.Select(c => new object?[] { PersianLabels.Health(c.Name), c.Count })),
            Section("پروژه‌های در حال اجرا (ابتدا بدترین)",
                [Text("کد"), Text("پروژه"), Text("وضعیت"), Text("مدیر پروژه"), Text("سلامت"), Number("پیشرفت برنامه‌ریزی‌شده (٪)"), Number("پیشرفت واقعی (٪)"),
                 Number("شاخص برنامه (SPI)"), Number("شاخص هزینه (CPI)"), Number("تأخیر پیش‌بینی‌شده (روز)"), Text("عقب‌افتاده از موعد")],
                board.Projects.Select(p => new object?[]
                {
                    p.Code, p.Name, PersianLabels.ProjectStatus(p.Status), names.Of(p.ManagerUserId), PersianLabels.Health(p.Health),
                    p.PlannedProgress, p.ActualProgress, p.Spi, p.Cpi, p.ForecastDelayDays, YesNo(p.IsOverdue)
                }))
        ]);

    public static ReportDocument Attention(AttentionBoardDto board, UserNames names)
    {
        var sections = new List<ReportSection>
        {
            Section("خلاصه", [Text("نوع"), Number("تعداد")],
                board.CountsByKind.Select(c => new object?[] { PersianLabels.AttentionKind(c.Name), c.Count })),
            Section(board.Truncated ? $"اقلام (فقط {board.Items.Count} مورد اول از {board.Total})" : "اقلام",
                [Text("شدت"), Text("نوع"), Text("موضوع"), Text("عنوان"), Text("پروژه"), Date("موعد"), Number("روز تأخیر"), Text("مسئول")],
                board.Items.Select(i => new object?[]
                {
                    PersianLabels.Severity(i.Severity), PersianLabels.AttentionKind(i.Kind), PersianLabels.SubjectType(i.SubjectType), i.Title,
                    i.ProjectName, i.DueDate, i.DaysLate, names.Of(i.OwnerUserId)
                }))
        };

        return new ReportDocument("اقلام نیازمند توجه", board.RisksIncluded ? Subtitle(board.AsOf) : Subtitle(board.AsOf) + " (ماژول ریسک نصب نیست)", sections);
    }

    public static ReportDocument Risks(RiskOverviewDto overview, UserNames names) =>
        new("نمای کلی ریسک‌ها", Subtitle(overview.AsOf),
        [
            Section("خلاصه", [Text("شاخص"), Number("تعداد")],
            [
                ["کل ریسک‌های تأییدشده", overview.Total], ["سطح زیاد", overview.High], ["سطح متوسط", overview.Medium], ["سطح کم", overview.Low],
                ["زیاد و هنوز باز", overview.OpenHigh], ["زیاد، باز و بدون برنامه پاسخ", overview.OpenHighWithoutResponsePlan]
            ]),
            Section("وضعیت پاسخ به ریسک", [Text("وضعیت"), Number("تعداد")],
                overview.ByResponseStatus.Select(s => new object?[] { PersianLabels.RiskResponse(s.Name), s.Count })),
            Section("ریسک‌ها به تفکیک پروژه",
                [Text("کد"), Text("پروژه"), Number("کل"), Number("زیاد"), Number("متوسط"), Number("کم"), Number("زیاد و باز")],
                overview.Projects.Select(p => new object?[] { p.Code, p.Name, p.Total, p.High, p.Medium, p.Low, p.OpenHigh })),
            Section("مهم‌ترین ریسک‌های باز",
                [Text("پروژه"), Text("ریسک"), Number("احتمال"), Number("اثر"), Number("عدد اولویت ریسک (RPN)"), Text("سطح"), Text("وضعیت پاسخ"), Text("مسئول ریسک"), Text("برنامه پاسخ")],
                overview.TopRisks.Select(r => new object?[]
                {
                    r.ProjectName, r.Description, r.ProbabilityScore, r.ImpactScore, r.Rpn, PersianLabels.RiskLevel(r.Level),
                    PersianLabels.RiskResponse(r.ResponseStatus), names.Of(r.RiskOwnerUserId), YesNo(r.HasResponsePlan)
                }))
        ]);

    public static ReportDocument Kpis(KpiOverviewDto overview) =>
        new("شاخص‌های کلیدی عملکرد", Subtitle(overview.AsOf),
        [
            Section("خلاصه", [Text("شاخص"), Number("مقدار")],
            [
                ["تعداد شاخص‌ها", overview.KpiCount], ["دارای مقدار اندازه‌گیری‌شده", overview.WithValues], ["بدون مقدار", overview.WithoutValues],
                ["عالی", overview.Excellent], ["خوب", overview.Good], ["نیازمند بهبود", overview.NeedsImprovement],
                ["میانگین تحقق (٪)", overview.AverageAchievement]
            ]),
            Section("شاخص‌ها",
                [Text("پروژه"), Text("شاخص"), Text("نوع"), Text("فرمول"), Number("مقدار هدف"), Number("آخرین مقدار"), Date("تاریخ آخرین اندازه‌گیری"),
                 Number("تعداد اندازه‌گیری"), Number("درصد تحقق (٪)"), Text("ارزیابی")],
                overview.Kpis.Select(k => new object?[]
                {
                    $"{k.ProjectCode} - {k.ProjectName}", k.Description, PersianLabels.KpiType(k.Type), k.Formula, k.TargetValue, k.LatestValue,
                    k.LatestMeasuredOn, k.MeasurementCount, k.AchievementPercent, PersianLabels.Rating(k.Rating)
                }))
        ]);
}
