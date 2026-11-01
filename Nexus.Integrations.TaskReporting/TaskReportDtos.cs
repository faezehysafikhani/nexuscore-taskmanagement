namespace Nexus.Integrations.TaskReporting;

/// <summary>A person in a report: name, and their position when the organization structure has one.</summary>
public sealed record ReportPerson(Guid UserId, string DisplayName, string? PositionTitle);

/// <summary>Whose tasks the caller may report on. Relation: "self", "direct", "indirect", or "all" (Tasks.ManageAll).</summary>
public sealed record ReportScopePerson(Guid UserId, string DisplayName, string? PositionTitle, string Relation, int Depth);

public sealed record ReportPosition(Guid Id, string Title);

/// <summary>
/// One stretch of time someone was on the task through one of its posts (where tasks are given to
/// posts): Role "creator", "responsible" or "acting" (stand-in, جانشین, of its vacant post), with
/// the position they had then and the task's post then (title, code, where). ToUtc null: still on
/// it; for a done task, its periods end when it was done at the latest.
/// </summary>
public sealed record ReportPeriod(
    Guid UserId,
    string DisplayName,
    string Role,
    string PositionTitle,
    string PostTitle,
    string PostCode,
    string Place,
    DateTimeOffset FromUtc,
    DateTimeOffset? ToUtc);

public sealed record TaskReportScopeDto(
    bool SeesAll,
    IReadOnlyList<ReportScopePerson> People,
    IReadOnlyList<ReportPosition> Positions,
    IReadOnlyList<string> Tags);

/// <summary>
/// Filters, all optional. PersonRole: "responsible" (default) or "creator" - applies to both the
/// person and the position filter. Where tasks are given to posts, a person or position also
/// finds the tasks they were on before (a stand-in's included, as responsible), by the position
/// they had then. Type: "normal", "project" or "recurring". Statuses: any of
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
    bool CanOpen,
    /// <summary>Who was on the task when, oldest first (empty where tasks are given to people).</summary>
    IReadOnlyList<ReportPeriod>? History = null);

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
