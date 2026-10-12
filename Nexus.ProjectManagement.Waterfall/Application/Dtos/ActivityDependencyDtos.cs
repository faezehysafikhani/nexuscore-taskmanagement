using Nexus.ProjectManagement.Waterfall.Domain;

namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

public sealed record ActivityDependencyDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid PredecessorActivityId, Guid SuccessorActivityId,
    DependencyType Type, int LagDays);

public sealed record CreateActivityDependencyRequest(
    Guid TenantId, Guid ProjectId, Guid PredecessorActivityId, Guid SuccessorActivityId,
    DependencyType Type = DependencyType.FinishToStart, int LagDays = 0);

public sealed record UpdateActivityDependencyRequest(DependencyType Type, int LagDays);

/// <summary>Makes <c>PredecessorActivityId</c> the one predecessor of the successor, replacing whatever links led into it; a null
/// predecessor just removes them. For screens that give an activity a single "prerequisite" with a type and a lag.</summary>
public sealed record SetActivityPredecessorRequest(
    Guid TenantId, Guid ProjectId, Guid SuccessorActivityId, Guid? PredecessorActivityId,
    DependencyType Type = DependencyType.FinishToStart, int LagDays = 0);
