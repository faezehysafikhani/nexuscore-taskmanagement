namespace Nexus.Portfolio.Application.Dtos;

public sealed record PortfolioProjectItem(
    Guid Id, string Name, string Code, string Type, string Status,
    Guid? OrganizationUnitId, Guid? ManagerUserId, Guid? OwnerUserId, string ApprovalStatus,
    DateOnly? StartDate = null, DateOnly? EndDate = null);

public sealed record PortfolioActionItem(
    Guid Id, string Title, string Status, Guid OrganizationUnitId,
    Guid? ResponsibleUserId, Guid? OwnerUserId, string ApprovalStatus,
    DateOnly? StartDate = null, DateOnly? EndDate = null);

public sealed record PortfolioResultDto(IReadOnlyList<PortfolioProjectItem> Projects, IReadOnlyList<PortfolioActionItem> Actions);

/// <summary>
/// ViewAll must only ever be set true by a caller that has already been authorized for
/// Portfolio.ViewAll at the endpoint - see PortfolioEndpoints. When false, results are always
/// filtered server-side to items CurrentUserId owns, manages, or is responsible for (real
/// backend filtering, not a UI hint) - rule: "صرف Hide کردن UI کافی نیست".
///
/// The optional trailing filters (all null = no narrowing) are applied on top of - never instead
/// of - that visibility rule. Search matches a project's name or code and an action's title.
/// InvolvedUserId matches a project's owner or manager and an action's owner or responsible.
/// Type is "Waterfall" or "Agile" (projects only; actions are then left out) or "Action"
/// (actions only; projects are then left out). Status and ApprovalStatus are matched against
/// the enum names, like Status already was.
/// </summary>
public sealed record PortfolioQuery(
    Guid TenantId,
    Guid CurrentUserId,
    bool ViewAll,
    Guid? OrganizationUnitId,
    string? Status,
    string? Search = null,
    Guid? InvolvedUserId = null,
    string? Type = null,
    string? ApprovalStatus = null);

