namespace Nexus.Portfolio.Application.Dtos;

public sealed record PortfolioProjectItem(
    Guid Id, string Name, string Code, string Type, string Status,
    Guid? OrganizationUnitId, Guid? ManagerUserId, Guid? OwnerUserId, string ApprovalStatus);

public sealed record PortfolioActionItem(
    Guid Id, string Title, string Status, Guid OrganizationUnitId,
    Guid? ResponsibleUserId, Guid? OwnerUserId, string ApprovalStatus);

public sealed record PortfolioResultDto(IReadOnlyList<PortfolioProjectItem> Projects, IReadOnlyList<PortfolioActionItem> Actions);

/// <summary>
/// ViewAll must only ever be set true by a caller that has already been authorized for
/// Portfolio.ViewAll at the endpoint - see PortfolioEndpoints. When false, results are always
/// filtered server-side to items CurrentUserId owns, manages, or is responsible for (real
/// backend filtering, not a UI hint) - rule: "صرف Hide کردن UI کافی نیست".
/// </summary>
public sealed record PortfolioQuery(
    Guid TenantId,
    Guid CurrentUserId,
    bool ViewAll,
    Guid? OrganizationUnitId,
    string? Status);
