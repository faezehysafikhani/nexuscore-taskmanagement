using Nexus.ProjectManagement.Deliverables.Domain;

namespace Nexus.ProjectManagement.Deliverables.Application.Dtos;

public sealed record DeliverableDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Title, string? Description, string? AcceptanceCriteria,
    Guid? ResponsibleUserId, DateOnly? TargetDate, DeliverableStatus Status);

public sealed record CreateDeliverableRequest(
    Guid TenantId, Guid ProjectId, string Title, string? Description, string? AcceptanceCriteria,
    Guid? ResponsibleUserId, DateOnly? TargetDate);

public sealed record UpdateDeliverableRequest(
    string Title, string? Description, string? AcceptanceCriteria, Guid? ResponsibleUserId, DateOnly? TargetDate);

public sealed record ChangeDeliverableStatusRequest(DeliverableStatus Status);
