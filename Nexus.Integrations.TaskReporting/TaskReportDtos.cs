namespace Nexus.Integrations.TaskReporting;

/// <summary>A person in a report: name, and their position when the organization structure has one.</summary>
public sealed record ReportPerson(Guid UserId, string DisplayName, string? PositionTitle);

/// <summary>Whose tasks the caller may report on. Relation: "self", "direct", "indirect", or "all" (Tasks.ManageAll).</summary>
public sealed record ReportScopePerson(Guid UserId, string DisplayName, string? PositionTitle, string Relation, int Depth);

public sealed record ReportPosition(Guid Id, string Title);

public sealed record TaskReportScopeDto(
    bool SeesAll,
    IReadOnlyList<ReportScopePerson> People,
    IReadOnlyList<ReportPosition> Positions,
    IReadOnlyList<string> Tags);

/// <summary>
/// Filters, all optional. PersonRole: "responsible" (default) or "creator" - applies to both the
/// person and the position filter. Type: "normal", "project" or "recurring". Statuses: any of
/// "todo", "in_progress", "paused", "completed". Due dates are inclusive, yyyy-MM-dd.
/// </summary>
public sealed record TaskReportQuery(
    Guid? PersonId,
    string? PersonRole,
    Guid? PositionId,
    string? Tag,
    string? Type,
    IReadOnlyList<string> Statuses,
    DateOnly? DueFrom,
    DateOnly? DueTo);

public sealed record TaskReportRow(
    Guid Id,
    string Title,
    string? Description,
    string Status,
    string Type,
    string Priority,
    string DueDate,
    string? DueTime,
    bool IsOverdue,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    ReportPerson? Owner,
    IReadOnlyList<ReportPerson> Responsible,
    IReadOnlyList<string> Tags,
    /// <summary>Whether the caller can open the task itself (owner, responsible, on its access list); otherwise it is in the report only as a manager's view.</summary>
    bool CanOpen);

public sealed record TaskReportSummary(
    int Total,
    int Todo,
    int InProgress,
    int Paused,
    int Completed,
    int Overdue,
    int Normal,
    int Project,
    int Recurring);

public sealed record TaskReportDto(TaskReportSummary Summary, IReadOnlyList<TaskReportRow> Rows, bool Truncated);
