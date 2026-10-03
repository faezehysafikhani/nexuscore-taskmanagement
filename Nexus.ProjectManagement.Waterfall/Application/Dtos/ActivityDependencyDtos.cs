using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

public sealed record ActivityDependencyDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid PredecessorActivityId, Guid SuccessorActivityId,
    DependencyType Type, int LagDays);

public sealed record CreateActivityDependencyRequest(
    Guid TenantId, Guid ProjectId, Guid PredecessorActivityId, Guid SuccessorActivityId,
    DependencyType Type = DependencyType.FinishToStart, int LagDays = 0);

public sealed record UpdateActivityDependencyRequest(DependencyType Type, int LagDays);
