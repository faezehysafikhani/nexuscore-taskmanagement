using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>This module's own unit of work (see IOrganizationUnitOfWork for why each module has one).</summary>
public interface IOrgStructureUnitOfWork : IUnitOfWork;

public interface IOrgStructureRepository
{
    // The dynamic structure's data (read only for now: nothing uses it yet).
    Task<IReadOnlyList<NodeType>> ListNodeTypesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NodeTypeRule>> ListNodeTypeRulesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NodeTypePostTemplate>> ListNodeTypePostTemplatesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LinkType>> ListLinkTypesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AutoLinkRule>> ListAutoLinkRulesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrgLink>> ListOrgLinksAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StructureTemplate>> ListStructureTemplatesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StructureTemplateItem>> ListStructureTemplateItemsAsync(Guid tenantId, Guid? templateId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrgChangeLog>> ListOrgChangeLogsAsync(Guid tenantId, Guid? entityId, int take, CancellationToken cancellationToken);
    Task AddNodeTypeAsync(NodeType nodeType, CancellationToken cancellationToken);
    void RemoveNodeType(NodeType nodeType);
    Task AddNodeTypeRuleAsync(NodeTypeRule rule, CancellationToken cancellationToken);
    void RemoveNodeTypeRule(NodeTypeRule rule);
    Task AddOrgChangeLogAsync(OrgChangeLog entry, CancellationToken cancellationToken);
    Task AddLinkTypeAsync(LinkType linkType, CancellationToken cancellationToken);
    void RemoveLinkType(LinkType linkType);
    Task AddAutoLinkRuleAsync(AutoLinkRule rule, CancellationToken cancellationToken);
    void RemoveAutoLinkRule(AutoLinkRule rule);
    Task AddOrgLinkAsync(OrgLink link, CancellationToken cancellationToken);
    void RemoveOrgLink(OrgLink link);
    Task AddNodeTypePostTemplateAsync(NodeTypePostTemplate template, CancellationToken cancellationToken);
    void RemoveNodeTypePostTemplate(NodeTypePostTemplate template);
    Task AddStructureTemplateAsync(StructureTemplate template, CancellationToken cancellationToken);
    void RemoveStructureTemplate(StructureTemplate template);

    Task<IReadOnlyList<Company>> ListCompaniesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Company?> GetCompanyAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<bool> CompanyCodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken cancellationToken);
    Task AddCompanyAsync(Company company, CancellationToken cancellationToken);
    void RemoveCompany(Company company);

    Task<IReadOnlyList<CompanyUnit>> ListCompanyUnitsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<CompanyUnit?> GetCompanyUnitAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken);
    Task AddCompanyUnitAsync(CompanyUnit link, CancellationToken cancellationToken);
    void RemoveCompanyUnit(CompanyUnit link);

    Task<IReadOnlyList<Position>> ListPositionsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Position?> GetPositionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<bool> PositionTitleExistsAsync(Guid tenantId, string title, Guid? excludeId, CancellationToken cancellationToken);
    Task AddPositionAsync(Position position, CancellationToken cancellationToken);
    void RemovePosition(Position position);

    Task<IReadOnlyList<UserAssignment>> ListAssignmentsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<UserAssignment?> GetAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task AddAssignmentAsync(UserAssignment assignment, CancellationToken cancellationToken);
    void RemoveAssignment(UserAssignment assignment);

    Task<IReadOnlyList<PositionCompany>> ListPositionCompaniesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddPositionCompanyAsync(PositionCompany positionCompany, CancellationToken cancellationToken);
    void RemovePositionCompany(PositionCompany positionCompany);

    Task<IReadOnlyList<TaskPost>> ListTaskPostsAsync(Guid tenantId, IReadOnlyCollection<Guid>? taskIds, IReadOnlyCollection<Guid>? postIds, CancellationToken cancellationToken);
    Task AddTaskPostAsync(TaskPost taskPost, CancellationToken cancellationToken);
    void RemoveTaskPost(TaskPost taskPost);

    /// <summary>The periods of these tasks (null: every task); only those not ended yet when <paramref name="openOnly"/>.</summary>
    Task<IReadOnlyList<TaskPostPeriod>> ListTaskPostPeriodsAsync(Guid tenantId, IReadOnlyCollection<Guid>? taskIds, bool openOnly, CancellationToken cancellationToken);

    /// <summary>The periods of these people (null: anyone) or of people with this position.</summary>
    Task<IReadOnlyList<TaskPostPeriod>> FindTaskPostPeriodsAsync(Guid tenantId, IReadOnlyCollection<Guid>? userIds, Guid? positionId, CancellationToken cancellationToken);

    Task AddTaskPostPeriodAsync(TaskPostPeriod period, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PostActing>> ListPostActingsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddPostActingAsync(PostActing acting, CancellationToken cancellationToken);
    void RemovePostActing(PostActing acting);

    Task<IReadOnlyList<UnitPosition>> ListUnitPositionsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddUnitPositionAsync(UnitPosition unitPosition, CancellationToken cancellationToken);
    void RemoveUnitPosition(UnitPosition unitPosition);

    /// <summary>Who holds which post now (appointments started and not ended).</summary>
    Task<IReadOnlyList<UserPosition>> ListUserPositionsAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Every appointment, ended and future ones too (each filter null: all).</summary>
    Task<IReadOnlyList<UserPosition>> ListUserPositionHistoryAsync(Guid tenantId, Guid? userId, Guid? unitPositionId, CancellationToken cancellationToken);
    Task AddUserPositionAsync(UserPosition userPosition, CancellationToken cancellationToken);
    /// <summary>Ends the appointment now (it stays as history); one not started yet is removed.</summary>
    void RemoveUserPosition(UserPosition userPosition);

    Task<IReadOnlyList<Honorific>> ListHonorificsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Honorific?> GetHonorificAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<bool> HonorificTitleExistsAsync(Guid tenantId, string title, Guid? excludeId, CancellationToken cancellationToken);
    Task AddHonorificAsync(Honorific honorific, CancellationToken cancellationToken);
    void RemoveHonorific(Honorific honorific);

    Task<IReadOnlyList<UserHonorific>> ListUserHonorificsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<UserHonorific?> GetUserHonorificAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task AddUserHonorificAsync(UserHonorific userHonorific, CancellationToken cancellationToken);
    void RemoveUserHonorific(UserHonorific userHonorific);
}
