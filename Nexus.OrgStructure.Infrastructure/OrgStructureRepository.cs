using Microsoft.EntityFrameworkCore;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Domain;

namespace Nexus.OrgStructure.Infrastructure;

public sealed class OrgStructureRepository(OrgStructureDbContext dbContext, TimeProvider? clock = null) : IOrgStructureRepository
{
    public async Task<IReadOnlyList<NodeType>> ListNodeTypesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.NodeTypes.Where(x => x.TenantId == tenantId).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NodeTypeRule>> ListNodeTypeRulesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.NodeTypeRules.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NodeTypePostTemplate>> ListNodeTypePostTemplatesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.NodeTypePostTemplates.Where(x => x.TenantId == tenantId).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LinkType>> ListLinkTypesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.LinkTypes.Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AutoLinkRule>> ListAutoLinkRulesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.AutoLinkRules.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrgLink>> ListOrgLinksAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.OrgLinks.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StructureTemplate>> ListStructureTemplatesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.StructureTemplates.Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StructureTemplateItem>> ListStructureTemplateItemsAsync(Guid tenantId, Guid? templateId, CancellationToken cancellationToken) =>
        await dbContext.StructureTemplateItems.Where(x => x.TenantId == tenantId && (templateId == null || x.TemplateId == templateId))
            .OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrgChangeLog>> ListOrgChangeLogsAsync(Guid tenantId, Guid? entityId, int take, CancellationToken cancellationToken) =>
        await dbContext.OrgChangeLogs.Where(x => x.TenantId == tenantId && (entityId == null || x.EntityId == entityId))
            .OrderByDescending(x => x.AtUtc).Take(take).ToListAsync(cancellationToken);

    public async Task AddNodeTypeAsync(NodeType nodeType, CancellationToken cancellationToken) =>
        await dbContext.NodeTypes.AddAsync(nodeType, cancellationToken);

    public void RemoveNodeType(NodeType nodeType) => dbContext.NodeTypes.Remove(nodeType);

    public async Task AddNodeTypeRuleAsync(NodeTypeRule rule, CancellationToken cancellationToken) =>
        await dbContext.NodeTypeRules.AddAsync(rule, cancellationToken);

    public void RemoveNodeTypeRule(NodeTypeRule rule) => dbContext.NodeTypeRules.Remove(rule);

    public async Task AddOrgChangeLogAsync(OrgChangeLog entry, CancellationToken cancellationToken) =>
        await dbContext.OrgChangeLogs.AddAsync(entry, cancellationToken);

    public async Task AddLinkTypeAsync(LinkType linkType, CancellationToken cancellationToken) => await dbContext.LinkTypes.AddAsync(linkType, cancellationToken);

    public void RemoveLinkType(LinkType linkType) => dbContext.LinkTypes.Remove(linkType);

    public async Task AddAutoLinkRuleAsync(AutoLinkRule rule, CancellationToken cancellationToken) => await dbContext.AutoLinkRules.AddAsync(rule, cancellationToken);

    public void RemoveAutoLinkRule(AutoLinkRule rule) => dbContext.AutoLinkRules.Remove(rule);

    public async Task AddOrgLinkAsync(OrgLink link, CancellationToken cancellationToken) => await dbContext.OrgLinks.AddAsync(link, cancellationToken);

    public void RemoveOrgLink(OrgLink link) => dbContext.OrgLinks.Remove(link);

    public async Task AddNodeTypePostTemplateAsync(NodeTypePostTemplate template, CancellationToken cancellationToken) =>
        await dbContext.NodeTypePostTemplates.AddAsync(template, cancellationToken);

    public void RemoveNodeTypePostTemplate(NodeTypePostTemplate template) => dbContext.NodeTypePostTemplates.Remove(template);

    public async Task AddStructureTemplateAsync(StructureTemplate template, CancellationToken cancellationToken) =>
        await dbContext.StructureTemplates.AddAsync(template, cancellationToken);

    public void RemoveStructureTemplate(StructureTemplate template) => dbContext.StructureTemplates.Remove(template);

    public async Task<IReadOnlyList<Company>> ListCompaniesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Companies.Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public Task<Company?> GetCompanyAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        dbContext.Companies.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<bool> CompanyCodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.Companies.AnyAsync(x => x.TenantId == tenantId && x.Code == code && x.Id != excludeId, cancellationToken);

    public async Task AddCompanyAsync(Company company, CancellationToken cancellationToken) =>
        await dbContext.Companies.AddAsync(company, cancellationToken);

    public void RemoveCompany(Company company) => dbContext.Companies.Remove(company);

    public async Task<IReadOnlyList<CompanyUnit>> ListCompanyUnitsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.CompanyUnits.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public Task<CompanyUnit?> GetCompanyUnitAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken) =>
        dbContext.CompanyUnits.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UnitId == unitId, cancellationToken);

    public async Task AddCompanyUnitAsync(CompanyUnit link, CancellationToken cancellationToken) =>
        await dbContext.CompanyUnits.AddAsync(link, cancellationToken);

    public void RemoveCompanyUnit(CompanyUnit link) => dbContext.CompanyUnits.Remove(link);

    public async Task<IReadOnlyList<Position>> ListPositionsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Positions.Where(x => x.TenantId == tenantId).OrderBy(x => x.Title).ToListAsync(cancellationToken);

    public Task<Position?> GetPositionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        dbContext.Positions.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<bool> PositionTitleExistsAsync(Guid tenantId, string title, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.Positions.AnyAsync(x => x.TenantId == tenantId && x.Title == title && x.Id != excludeId, cancellationToken);

    public async Task AddPositionAsync(Position position, CancellationToken cancellationToken) =>
        await dbContext.Positions.AddAsync(position, cancellationToken);

    public void RemovePosition(Position position) => dbContext.Positions.Remove(position);

    public async Task<IReadOnlyList<UserAssignment>> ListAssignmentsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.UserAssignments.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public Task<UserAssignment?> GetAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserAssignments.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);

    public async Task AddAssignmentAsync(UserAssignment assignment, CancellationToken cancellationToken) =>
        await dbContext.UserAssignments.AddAsync(assignment, cancellationToken);

    public void RemoveAssignment(UserAssignment assignment) => dbContext.UserAssignments.Remove(assignment);

    public async Task<IReadOnlyList<PositionCompany>> ListPositionCompaniesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.PositionCompanies.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public async Task AddPositionCompanyAsync(PositionCompany positionCompany, CancellationToken cancellationToken) =>
        await dbContext.PositionCompanies.AddAsync(positionCompany, cancellationToken);

    public void RemovePositionCompany(PositionCompany positionCompany) => dbContext.PositionCompanies.Remove(positionCompany);

    public async Task<IReadOnlyList<TaskPost>> ListTaskPostsAsync(
        Guid tenantId, IReadOnlyCollection<Guid>? taskIds, IReadOnlyCollection<Guid>? postIds, CancellationToken cancellationToken)
    {
        var query = dbContext.TaskPosts.Where(x => x.TenantId == tenantId);
        if (taskIds is not null)
        {
            query = query.Where(x => taskIds.Contains(x.TaskId));
        }

        if (postIds is not null)
        {
            query = query.Where(x => postIds.Contains(x.PostId));
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task AddTaskPostAsync(TaskPost taskPost, CancellationToken cancellationToken) =>
        await dbContext.TaskPosts.AddAsync(taskPost, cancellationToken);

    public void RemoveTaskPost(TaskPost taskPost) => dbContext.TaskPosts.Remove(taskPost);

    public async Task<IReadOnlyList<TaskPostPeriod>> ListTaskPostPeriodsAsync(
        Guid tenantId, IReadOnlyCollection<Guid>? taskIds, bool openOnly, CancellationToken cancellationToken)
    {
        var query = dbContext.TaskPostPeriods.Where(x => x.TenantId == tenantId);
        if (taskIds is not null)
        {
            query = query.Where(x => taskIds.Contains(x.TaskId));
        }

        if (openOnly)
        {
            query = query.Where(x => x.EndedAtUtc == null);
        }

        return await query.OrderBy(x => x.StartedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TaskPostPeriod>> FindTaskPostPeriodsAsync(
        Guid tenantId, IReadOnlyCollection<Guid>? userIds, Guid? positionId, CancellationToken cancellationToken)
    {
        var query = dbContext.TaskPostPeriods.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (userIds is not null)
        {
            query = query.Where(x => userIds.Contains(x.UserId));
        }

        if (positionId is { } position)
        {
            query = query.Where(x => x.PositionId == position);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task AddTaskPostPeriodAsync(TaskPostPeriod period, CancellationToken cancellationToken) =>
        await dbContext.TaskPostPeriods.AddAsync(period, cancellationToken);

    public async Task<IReadOnlyList<PostActing>> ListPostActingsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.PostActings.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public async Task AddPostActingAsync(PostActing acting, CancellationToken cancellationToken) =>
        await dbContext.PostActings.AddAsync(acting, cancellationToken);

    public void RemovePostActing(PostActing acting) => dbContext.PostActings.Remove(acting);

    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken) =>
        await dbContext.Companies.Select(x => x.TenantId).Distinct().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UnitPosition>> ListUnitPositionsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.UnitPositions.Where(x => x.TenantId == tenantId).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);

    public async Task AddUnitPositionAsync(UnitPosition unitPosition, CancellationToken cancellationToken) =>
        await dbContext.UnitPositions.AddAsync(unitPosition, cancellationToken);

    public void RemoveUnitPosition(UnitPosition unitPosition) => dbContext.UnitPositions.Remove(unitPosition);

    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();

    /// <summary>The appointments on now (started, not ended): what every reader of «who holds which post» sees.</summary>
    public async Task<IReadOnlyList<UserPosition>> ListUserPositionsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var now = Now;
        return await dbContext.UserPositions
            .Where(x => x.TenantId == tenantId && (x.StartDate == null || x.StartDate <= now) && (x.EndDate == null || x.EndDate > now))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserPosition>> ListUserPositionHistoryAsync(Guid tenantId, Guid? userId, Guid? unitPositionId, CancellationToken cancellationToken) =>
        await dbContext.UserPositions
            .Where(x => x.TenantId == tenantId && (userId == null || x.UserId == userId) && (unitPositionId == null || x.UnitPositionId == unitPositionId))
            .ToListAsync(cancellationToken);

    public async Task AddUserPositionAsync(UserPosition userPosition, CancellationToken cancellationToken)
    {
        userPosition.StartOn(Now);
        await dbContext.UserPositions.AddAsync(userPosition, cancellationToken);
    }

    /// <summary>
    /// Ends the appointment now: it stays as history. One that has not started yet never was, so it goes.
    /// </summary>
    public void RemoveUserPosition(UserPosition userPosition)
    {
        var now = Now;
        if (userPosition.StartDate is { } start && start > now)
        {
            dbContext.UserPositions.Remove(userPosition);
            return;
        }

        if (userPosition.EndDate is not { } end || end > now)
        {
            userPosition.End(now);
        }
    }

    public async Task<IReadOnlyList<Honorific>> ListHonorificsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Honorifics.Where(x => x.TenantId == tenantId).OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToListAsync(cancellationToken);

    public Task<Honorific?> GetHonorificAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        dbContext.Honorifics.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<bool> HonorificTitleExistsAsync(Guid tenantId, string title, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.Honorifics.AnyAsync(x => x.TenantId == tenantId && x.Title == title && x.Id != excludeId, cancellationToken);

    public async Task AddHonorificAsync(Honorific honorific, CancellationToken cancellationToken) =>
        await dbContext.Honorifics.AddAsync(honorific, cancellationToken);

    public void RemoveHonorific(Honorific honorific) => dbContext.Honorifics.Remove(honorific);

    public async Task<IReadOnlyList<UserHonorific>> ListUserHonorificsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.UserHonorifics.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);

    public Task<UserHonorific?> GetUserHonorificAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserHonorifics.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);

    public async Task AddUserHonorificAsync(UserHonorific userHonorific, CancellationToken cancellationToken) =>
        await dbContext.UserHonorifics.AddAsync(userHonorific, cancellationToken);

    public void RemoveUserHonorific(UserHonorific userHonorific) => dbContext.UserHonorifics.Remove(userHonorific);
}
