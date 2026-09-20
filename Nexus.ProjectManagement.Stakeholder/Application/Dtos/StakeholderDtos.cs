using Nexus.ProjectManagement.StakeholderManagement.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.StakeholderManagement.Application.Dtos;

public sealed record StakeholderDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Name, bool IsInternal,
    string? Expectations, string? Notes, PowerLevel Power, InterestLevel Interest,
    string? EngagementStrategy, string? Requirements, ApprovalStatus ApprovalStatus,
    Guid? CreatedByUserId);

public sealed record CreateStakeholderRequest(
    Guid TenantId, Guid ProjectId, string Name, bool IsInternal,
    string? Expectations, string? Notes, PowerLevel Power, InterestLevel Interest,
    string? EngagementStrategy, string? Requirements);

public sealed record UpdateStakeholderRequest(
    string Name, bool IsInternal, string? Expectations, string? Notes,
    PowerLevel Power, InterestLevel Interest, string? EngagementStrategy, string? Requirements);
