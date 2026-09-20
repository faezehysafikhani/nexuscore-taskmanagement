using Nexus.Actions.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.Actions.Application.Dtos;

public sealed record ActionItemDto(
    Guid Id, Guid TenantId, string Title, string? Description,
    Guid? OwnerUserId, Guid? ResponsibleUserId, ActionStatus Status,
    Guid OrganizationUnitId, Guid WorkCalendarId, Guid? ProjectId,
    DateOnly? StartDate, DateOnly? EndDate, ApprovalStatus ApprovalStatus);

public sealed record CreateActionItemRequest(
    Guid TenantId, string Title, string? Description,
    Guid? OwnerUserId, Guid? ResponsibleUserId,
    Guid OrganizationUnitId, Guid WorkCalendarId, Guid? ProjectId,
    DateOnly? StartDate, DateOnly? EndDate);

public sealed record UpdateActionItemRequest(
    string Title, string? Description, Guid? OwnerUserId, Guid? ResponsibleUserId,
    Guid OrganizationUnitId, Guid WorkCalendarId, Guid? ProjectId,
    DateOnly? StartDate, DateOnly? EndDate);

public sealed record ChangeActionStatusRequest(ActionStatus Status);
