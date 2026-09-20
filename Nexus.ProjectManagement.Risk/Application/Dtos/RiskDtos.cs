using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.RiskManagement.Application.Dtos;

public sealed record RiskDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Description,
    int ProbabilityScore, int SeverityScore, int ImpactScore, int Rpn,
    string? ResponsePlan, Guid? RiskOwnerUserId, ApprovalStatus ApprovalStatus,
    Guid? CreatedByUserId, DateTimeOffset CreatedAtUtc);

public sealed record CreateRiskRequest(
    Guid TenantId, Guid ProjectId, string Description,
    int ProbabilityScore, int SeverityScore, int ImpactScore,
    string? ResponsePlan, Guid? RiskOwnerUserId);

public sealed record UpdateRiskRequest(
    string Description, int ProbabilityScore, int SeverityScore, int ImpactScore,
    string? ResponsePlan, Guid? RiskOwnerUserId);
