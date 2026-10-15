using Nexus.Actions.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.Actions.Application.Dtos;

public sealed record ActionItemDto(
    Guid Id, Guid TenantId, string Title, string? Description,
    Guid? OwnerUserId, Guid? ResponsibleUserId, ActionStatus Status,
    Guid OrganizationUnitId, Guid WorkCalendarId, Guid? ProjectId,
    DateOnly? StartDate, DateOnly? EndDate, ApprovalStatus ApprovalStatus,
    ActionPriority Priority = ActionPriority.Normal,
    ActionRecurrenceDto? Recurrence = null, Guid? RecurrenceSourceId = null, Guid? NextOccurrenceId = null,
    string? SourceType = null, Guid? SourceId = null, IReadOnlyList<Guid>? ApproverUserIds = null);

/// <summary>The repeat rule of an action, or null on the action when it is a one-off.</summary>
public sealed record ActionRecurrenceDto(
    ActionRecurrenceUnit Unit, int Interval, DateOnly? EndDate,
    IReadOnlyList<int>? DaysOfWeek = null, int? DayOfMonth = null, IReadOnlyList<int>? Months = null, decimal? DurationHours = null);

/// <summary>
/// Omitted (null) on an update leaves the action's rule alone; a request whose Unit is null removes it.
/// The optional pattern fields describe which occurrences of the rule are meant, inside the action's own date range:
/// DaysOfWeek (0 = Sunday ... 6 = Saturday), DayOfMonth and Months (1-12) in the Jalali calendar, and how many hours each takes.
/// A request replaces the whole rule, so a field left out is cleared.
/// </summary>
public sealed record ActionRecurrenceRequest(
    ActionRecurrenceUnit? Unit, int Interval = 1, DateOnly? EndDate = null,
    IReadOnlyList<int>? DaysOfWeek = null, int? DayOfMonth = null, IReadOnlyList<int>? Months = null, decimal? DurationHours = null);

public sealed record CreateActionItemRequest(
    Guid TenantId, string Title, string? Description,
    Guid? OwnerUserId, Guid? ResponsibleUserId,
    Guid OrganizationUnitId, Guid WorkCalendarId, Guid? ProjectId,
    DateOnly? StartDate, DateOnly? EndDate, ActionPriority? Priority = null, ActionRecurrenceRequest? Recurrence = null,
    string? SourceType = null, Guid? SourceId = null, IReadOnlyList<Guid>? ApproverUserIds = null);

public sealed record UpdateActionItemRequest(
    string Title, string? Description, Guid? OwnerUserId, Guid? ResponsibleUserId,
    Guid OrganizationUnitId, Guid WorkCalendarId, Guid? ProjectId,
    DateOnly? StartDate, DateOnly? EndDate, ActionPriority? Priority = null, ActionRecurrenceRequest? Recurrence = null,
    string? SourceType = null, Guid? SourceId = null, IReadOnlyList<Guid>? ApproverUserIds = null);

public sealed record ChangeActionStatusRequest(ActionStatus Status);

/// <summary>One occurrence of a repeating action. Index starts at 1.</summary>
public sealed record ActionOccurrenceDto(int Index, DateOnly Date, decimal? DurationHours);

public sealed record ListActionsRequest(IReadOnlyList<Guid> ActionIds);

public sealed record SubmitActionStatusRequest(Guid TenantId, Guid ActionId, ActionStatus Status, string? Comment);

public sealed record ActionStatusSubmissionDto(
    Guid Id, Guid TenantId, Guid? ProjectId, Guid ActionId, ActionStatus PreviousStatus, ActionStatus SubmittedStatus, string? Comment,
    ApprovalStatus ApprovalStatus, Guid? SubmittedByUserId, DateTimeOffset SubmittedAtUtc);
