using Nexus.ProjectManagement.RiskManagement.Domain;
using NexusCore.Application.Approvals;

namespace Nexus.ProjectManagement.RiskManagement.Application.Dtos;

public sealed record RiskDto(
    Guid Id, Guid TenantId, Guid ProjectId, string Description,
    int ProbabilityScore, int SeverityScore, int ImpactScore, int Rpn,
    string? ResponsePlan, Guid? RiskOwnerUserId, ApprovalStatus ApprovalStatus,
    Guid? CreatedByUserId, DateTimeOffset CreatedAtUtc,
    string ProbabilityLevel = "medium", string ImpactLevel = "medium", string Level = "yellow",
    RiskResponseStatus ResponseStatus = RiskResponseStatus.Planning);

/// <summary>ProbabilityLevel / ImpactLevel ("low", "medium", "high") may be given instead of the 1-5 scores: a band stands for the score 1, 3 or 5
/// (severity follows the impact). When a level is given the matching score is ignored.</summary>
public sealed record CreateRiskRequest(
    Guid TenantId, Guid ProjectId, string Description,
    int ProbabilityScore, int SeverityScore, int ImpactScore,
    string? ResponsePlan, Guid? RiskOwnerUserId,
    string? ProbabilityLevel = null, string? ImpactLevel = null);

public sealed record UpdateRiskRequest(
    string Description, int ProbabilityScore, int SeverityScore, int ImpactScore,
    string? ResponsePlan, Guid? RiskOwnerUserId,
    string? ProbabilityLevel = null, string? ImpactLevel = null);

public sealed record ChangeRiskResponseStatusRequest(RiskResponseStatus Status);

public sealed record ListRisksForProjectsRequest(IReadOnlyList<Guid> ProjectIds);

public sealed record SubmitRiskResponseRequest(Guid TenantId, Guid RiskId, RiskResponseStatus Status, string? Comment);

public sealed record RiskResponseSubmissionDto(
    Guid Id, Guid TenantId, Guid ProjectId, Guid RiskId, RiskResponseStatus PreviousStatus, RiskResponseStatus SubmittedStatus, string? Comment,
    ApprovalStatus ApprovalStatus, Guid? SubmittedByUserId, DateTimeOffset SubmittedAtUtc);
