namespace NexusCore.Application.Identity.Interfaces;

/// <summary>A personal work team: who built it and who is in it.</summary>
public sealed record WorkTeamInfo(Guid Id, string Name, string? Description, Guid OwnerUserId, IReadOnlyList<Guid> MemberIds);

/// <summary>
/// Read-only view of the organization's active work teams (the user-group feature's personal
/// teams), for modules that show them - Nexus.OrgStructure's chart. Empty when the feature is off.
/// </summary>
public interface IWorkTeamDirectory
{
    Task<IReadOnlyList<WorkTeamInfo>> ListWorkTeamsAsync(Guid tenantId, CancellationToken cancellationToken);
}
