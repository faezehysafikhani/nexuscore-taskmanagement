using Nexus.ProjectManagement.StakeholderManagement.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.StakeholderManagement.Application.Dtos;

public sealed record StakeholderDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Name, bool IsInternal,
    string? Expectations, string? Notes, PowerLevel Power, InterestLevel Interest,
    string? EngagementStrategy, string? Requirements, ApprovalStatus ApprovalStatus,
    Guid? CreatedByUserId,
    StakeholderQuadrant Quadrant = StakeholderQuadrant.Monitor, string? Strategy = null);

public sealed record CreateStakeholderRequest(
    Guid TenantId, Guid ProjectId, string Name, bool IsInternal,
    string? Expectations, string? Notes, PowerLevel Power, InterestLevel Interest,
    string? EngagementStrategy, string? Requirements);

public sealed record UpdateStakeholderRequest(
    string Name, bool IsInternal, string? Expectations, string? Notes,
    PowerLevel Power, InterestLevel Interest, string? EngagementStrategy, string? Requirements);

public sealed record ListStakeholdersForProjectsRequest(IReadOnlyList<Guid> ProjectIds);

/// <summary>How a project words its strategy for each quadrant. IsDefault = it has set none of them.</summary>
public sealed record StakeholderSettingsDto(
    Guid ProjectId, string ManageClosely, string KeepSatisfied, string KeepInformed, string Monitor, bool IsDefault);

/// <summary>A null or blank text puts that quadrant back on the default.</summary>
public sealed record SaveStakeholderSettingsRequest(
    Guid TenantId, Guid ProjectId, string? ManageClosely, string? KeepSatisfied, string? KeepInformed, string? Monitor);

public sealed record StakeholderSettingsForProjectsRequest(IReadOnlyList<Guid> ProjectIds);
