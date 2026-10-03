using Nexus.Actions.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.Actions.Application.Dtos;

public sealed record ActionItemDto(
    Guid Id, Guid TenantId, string Title, string? Description,
    Guid? OwnerUserId, Guid? ResponsibleUserId, ActionStatus Status,
    Guid OrganizationUnitId, Guid WorkCalendarId, Guid? ProjectId,
    DateOnly? StartDate, DateOnly? EndDate, ApprovalStatus ApprovalStatus,
    ActionPriority Priority = ActionPriority.Normal,
    ActionRecurrenceDto? Recurrence = null, Guid? RecurrenceSourceId = null, Guid? NextOccurrenceId = null);

/// <summary>The repeat rule of an action, or null on the action when it is a one-off.</summary>
public sealed record ActionRecurrenceDto(ActionRecurrenceUnit Unit, int Interval, DateOnly? EndDate);

/// <summary>Omitted (null) on an update leaves the action's rule alone; a request whose Unit is null removes it.</summary>
public sealed record ActionRecurrenceRequest(ActionRecurrenceUnit? Unit, int Interval = 1, DateOnly? EndDate = null);

public sealed record CreateActionItemRequest(
    Guid TenantId, string Title, string? Description,
    Guid? OwnerUserId, Guid? ResponsibleUserId,
    Guid OrganizationUnitId, Guid WorkCalendarId, Guid? ProjectId,
    DateOnly? StartDate, DateOnly? EndDate, ActionPriority? Priority = null, ActionRecurrenceRequest? Recurrence = null);

public sealed record UpdateActionItemRequest(
    string Title, string? Description, Guid? OwnerUserId, Guid? ResponsibleUserId,
    Guid OrganizationUnitId, Guid WorkCalendarId, Guid? ProjectId,
    DateOnly? StartDate, DateOnly? EndDate, ActionPriority? Priority = null, ActionRecurrenceRequest? Recurrence = null);

public sealed record ChangeActionStatusRequest(ActionStatus Status);
