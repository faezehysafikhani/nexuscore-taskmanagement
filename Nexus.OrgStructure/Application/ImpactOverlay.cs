using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// A proposed change to the structure, laid over the real data for the impact preview: node
/// parents, company links, node managers and new (empty) nodes. Nothing else changes.
/// </summary>
public sealed class StructureOverlay
{
    public Dictionary<Guid, Guid?> Parents { get; } = [];
    public Dictionary<Guid, Guid?> Managers { get; } = [];

    /// <summary>Node → company: the node's link after the change (null: no link).</summary>
    public Dictionary<Guid, Guid?> Links { get; } = [];

    public List<OrganizationUnit> NewUnits { get; } = [];
}

/// <summary>
/// The real data read once and kept in memory, seen through an overlay (empty: as it is). Every
/// read returns copies of what the overlay changes, never the tracked rows themselves, and every
/// write is refused - the preview cannot save anything, by construction. Reading the data once
/// also lets the real consumers (task limit, work team, substitution...) run for every person
/// without asking the database again.
/// </summary>
public sealed class ImpactData(IOrgStructureRepository real, IOrganizationUnitRepository realUnits, Guid tenantId)
{
    private readonly Dictionary<string, object> _cache = [];

    private async Task<IReadOnlyList<T>> Cached<T>(string key, Func<Task<IReadOnlyList<T>>> load)
    {
        if (!_cache.TryGetValue(key, out var value))
        {
            _cache[key] = value = await load();
        }

        return (IReadOnlyList<T>)value;
    }

    public Guid TenantId => tenantId;

    public IOrgStructureRepository Structure(StructureOverlay overlay) => new OverlayRepository(this, overlay);

    public IOrganizationUnitRepository Units(StructureOverlay overlay) => new OverlayUnits(this, overlay);

    internal Task<IReadOnlyList<OrganizationUnit>> RealUnitsAsync(CancellationToken ct) => Cached("units", () => realUnits.ListAsync(tenantId, ct));
    internal Task<IReadOnlyList<CompanyUnit>> RealLinksAsync(CancellationToken ct) => Cached("links", () => real.ListCompanyUnitsAsync(tenantId, ct));
    internal Task<IReadOnlyList<T>> ReadAsync<T>(string key, Func<Task<IReadOnlyList<T>>> load) => Cached(key, load);

    internal static OrganizationUnit Copy(OrganizationUnit unit, StructureOverlay overlay)
    {
        var copy = new OrganizationUnit(unit.Id, unit.TenantId, unit.Name,
            overlay.Managers.TryGetValue(unit.Id, out var manager) ? manager : unit.ManagerUserId);
        copy.SetParent(overlay.Parents.TryGetValue(unit.Id, out var parent) ? parent : unit.ParentUnitId);
        copy.SetNodeType(unit.NodeTypeId);
        copy.SetSortOrder(unit.SortOrder);
        copy.SetValidity(unit.ValidFrom, unit.ValidTo);
        if (!unit.IsActive)
        {
            copy.Update(copy.Name, copy.ManagerUserId, isActive: false);
        }

        return copy;
    }

    private sealed class OverlayUnits(ImpactData data, StructureOverlay overlay) : IOrganizationUnitRepository
    {
        public async Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            (await ListAsync(data.TenantId, cancellationToken)).FirstOrDefault(unit => unit.Id == id);

        public async Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
            (await data.RealUnitsAsync(cancellationToken)).Select(unit => Copy(unit, overlay)).Concat(overlay.NewUnits).ToList();

        public Task AddAsync(OrganizationUnit unit, CancellationToken cancellationToken) => throw ReadOnly();
    }

    internal static InvalidOperationException ReadOnly() => new("The impact preview never writes.");

    /// <summary>Reads through the cache (links through the overlay); writes refused.</summary>
    private sealed class OverlayRepository(ImpactData data, StructureOverlay overlay) : IOrgStructureRepository
    {
        private Task<IReadOnlyList<T>> R<T>(string key, Func<Task<IReadOnlyList<T>>> load) => data.ReadAsync(key, load);

        public Task<IReadOnlyList<NodeType>> ListNodeTypesAsync(Guid t, CancellationToken ct) => R("nodeTypes", () => real.ListNodeTypesAsync(t, ct));
        public Task<IReadOnlyList<NodeTypeRule>> ListNodeTypeRulesAsync(Guid t, CancellationToken ct) => R("rules", () => real.ListNodeTypeRulesAsync(t, ct));
        public Task<IReadOnlyList<NodeTypePostTemplate>> ListNodeTypePostTemplatesAsync(Guid t, CancellationToken ct) => R("postTemplates", () => real.ListNodeTypePostTemplatesAsync(t, ct));
        public Task<IReadOnlyList<LinkType>> ListLinkTypesAsync(Guid t, CancellationToken ct) => R("linkTypes", () => real.ListLinkTypesAsync(t, ct));
        public Task<IReadOnlyList<AutoLinkRule>> ListAutoLinkRulesAsync(Guid t, CancellationToken ct) => R("autoLinks", () => real.ListAutoLinkRulesAsync(t, ct));
        public Task<IReadOnlyList<OrgLink>> ListOrgLinksAsync(Guid t, CancellationToken ct) => R("orgLinks", () => real.ListOrgLinksAsync(t, ct));
        public Task<IReadOnlyList<StructureTemplate>> ListStructureTemplatesAsync(Guid t, CancellationToken ct) => real.ListStructureTemplatesAsync(t, ct);
        public Task<IReadOnlyList<StructureTemplateItem>> ListStructureTemplateItemsAsync(Guid t, Guid? templateId, CancellationToken ct) => real.ListStructureTemplateItemsAsync(t, templateId, ct);
        public Task<IReadOnlyList<OrgChangeLog>> ListOrgChangeLogsAsync(Guid t, Guid? entityId, int take, CancellationToken ct) => real.ListOrgChangeLogsAsync(t, entityId, take, ct);

        public Task<IReadOnlyList<Company>> ListCompaniesAsync(Guid t, CancellationToken ct) => R("companies", () => real.ListCompaniesAsync(t, ct));
        public async Task<Company?> GetCompanyAsync(Guid t, Guid id, CancellationToken ct) => (await ListCompaniesAsync(t, ct)).FirstOrDefault(c => c.Id == id);
        public async Task<bool> CompanyCodeExistsAsync(Guid t, string code, Guid? excludeId, CancellationToken ct) =>
            (await ListCompaniesAsync(t, ct)).Any(c => c.Code == code && c.Id != excludeId);

        public async Task<IReadOnlyList<CompanyUnit>> ListCompanyUnitsAsync(Guid t, CancellationToken ct)
        {
            var links = (await data.RealLinksAsync(ct))
                .Where(link => !overlay.Links.ContainsKey(link.UnitId))
                .Select(link => new CompanyUnit(link.Id, link.TenantId, link.CompanyId, link.UnitId))
                .ToList();
            links.AddRange(overlay.Links.Where(pair => pair.Value is not null)
                .Select(pair => new CompanyUnit(Guid.NewGuid(), t, pair.Value!.Value, pair.Key)));
            return links;
        }

        public async Task<CompanyUnit?> GetCompanyUnitAsync(Guid t, Guid unitId, CancellationToken ct) =>
            (await ListCompanyUnitsAsync(t, ct)).FirstOrDefault(link => link.UnitId == unitId);

        public Task<IReadOnlyList<Position>> ListPositionsAsync(Guid t, CancellationToken ct) => R("positions", () => real.ListPositionsAsync(t, ct));
        public async Task<Position?> GetPositionAsync(Guid t, Guid id, CancellationToken ct) => (await ListPositionsAsync(t, ct)).FirstOrDefault(p => p.Id == id);
        public async Task<bool> PositionTitleExistsAsync(Guid t, string title, Guid? excludeId, CancellationToken ct) =>
            (await ListPositionsAsync(t, ct)).Any(p => p.Title == title && p.Id != excludeId);

        public Task<IReadOnlyList<UserAssignment>> ListAssignmentsAsync(Guid t, CancellationToken ct) => R("assignments", () => real.ListAssignmentsAsync(t, ct));
        public async Task<UserAssignment?> GetAssignmentAsync(Guid t, Guid userId, CancellationToken ct) => (await ListAssignmentsAsync(t, ct)).FirstOrDefault(a => a.UserId == userId);
        public Task<IReadOnlyList<PositionCompany>> ListPositionCompaniesAsync(Guid t, CancellationToken ct) => R("positionCompanies", () => real.ListPositionCompaniesAsync(t, ct));
        public Task<IReadOnlyList<TaskPost>> ListTaskPostsAsync(Guid t, IReadOnlyCollection<Guid>? taskIds, IReadOnlyCollection<Guid>? postIds, CancellationToken ct) => real.ListTaskPostsAsync(t, taskIds, postIds, ct);
        public Task<IReadOnlyList<TaskPostPeriod>> ListTaskPostPeriodsAsync(Guid t, IReadOnlyCollection<Guid>? taskIds, bool openOnly, CancellationToken ct) => real.ListTaskPostPeriodsAsync(t, taskIds, openOnly, ct);
        public Task<IReadOnlyList<TaskPostPeriod>> FindTaskPostPeriodsAsync(Guid t, IReadOnlyCollection<Guid>? userIds, Guid? positionId, CancellationToken ct) => real.FindTaskPostPeriodsAsync(t, userIds, positionId, ct);
        public Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken ct) => real.ListTenantIdsAsync(ct);
        public Task<IReadOnlyList<PostActing>> ListPostActingsAsync(Guid t, CancellationToken ct) => R("actings", () => real.ListPostActingsAsync(t, ct));
        public Task<IReadOnlyList<UnitPosition>> ListUnitPositionsAsync(Guid t, CancellationToken ct) => R("seats", () => real.ListUnitPositionsAsync(t, ct));
        public Task<IReadOnlyList<UserPosition>> ListUserPositionsAsync(Guid t, CancellationToken ct) => R("holdings", () => real.ListUserPositionsAsync(t, ct));
        public Task<IReadOnlyList<Honorific>> ListHonorificsAsync(Guid t, CancellationToken ct) => real.ListHonorificsAsync(t, ct);
        public Task<Honorific?> GetHonorificAsync(Guid t, Guid id, CancellationToken ct) => real.GetHonorificAsync(t, id, ct);
        public Task<bool> HonorificTitleExistsAsync(Guid t, string title, Guid? excludeId, CancellationToken ct) => real.HonorificTitleExistsAsync(t, title, excludeId, ct);
        public Task<IReadOnlyList<UserHonorific>> ListUserHonorificsAsync(Guid t, CancellationToken ct) => real.ListUserHonorificsAsync(t, ct);
        public Task<UserHonorific?> GetUserHonorificAsync(Guid t, Guid userId, CancellationToken ct) => real.GetUserHonorificAsync(t, userId, ct);

        // --- writes: never
        public Task AddNodeTypeAsync(NodeType nodeType, CancellationToken ct) => throw ReadOnly();
        public void RemoveNodeType(NodeType nodeType) => throw ReadOnly();
        public Task AddNodeTypeRuleAsync(NodeTypeRule rule, CancellationToken ct) => throw ReadOnly();
        public void RemoveNodeTypeRule(NodeTypeRule rule) => throw ReadOnly();
        public Task AddOrgChangeLogAsync(OrgChangeLog entry, CancellationToken ct) => throw ReadOnly();
        public Task AddCompanyAsync(Company company, CancellationToken ct) => throw ReadOnly();
        public void RemoveCompany(Company company) => throw ReadOnly();
        public Task AddCompanyUnitAsync(CompanyUnit link, CancellationToken ct) => throw ReadOnly();
        public void RemoveCompanyUnit(CompanyUnit link) => throw ReadOnly();
        public Task AddPositionAsync(Position position, CancellationToken ct) => throw ReadOnly();
        public void RemovePosition(Position position) => throw ReadOnly();
        public Task AddAssignmentAsync(UserAssignment assignment, CancellationToken ct) => throw ReadOnly();
        public void RemoveAssignment(UserAssignment assignment) => throw ReadOnly();
        public Task AddPositionCompanyAsync(PositionCompany positionCompany, CancellationToken ct) => throw ReadOnly();
        public void RemovePositionCompany(PositionCompany positionCompany) => throw ReadOnly();
        public Task AddTaskPostAsync(TaskPost taskPost, CancellationToken ct) => throw ReadOnly();
        public void RemoveTaskPost(TaskPost taskPost) => throw ReadOnly();
        public Task AddTaskPostPeriodAsync(TaskPostPeriod period, CancellationToken ct) => throw ReadOnly();
        public Task AddPostActingAsync(PostActing acting, CancellationToken ct) => throw ReadOnly();
        public void RemovePostActing(PostActing acting) => throw ReadOnly();
        public Task AddUnitPositionAsync(UnitPosition unitPosition, CancellationToken ct) => throw ReadOnly();
        public void RemoveUnitPosition(UnitPosition unitPosition) => throw ReadOnly();
        public Task AddUserPositionAsync(UserPosition userPosition, CancellationToken ct) => throw ReadOnly();
        public void RemoveUserPosition(UserPosition userPosition) => throw ReadOnly();
        public Task AddHonorificAsync(Honorific honorific, CancellationToken ct) => throw ReadOnly();
        public void RemoveHonorific(Honorific honorific) => throw ReadOnly();
        public Task AddUserHonorificAsync(UserHonorific userHonorific, CancellationToken ct) => throw ReadOnly();
        public void RemoveUserHonorific(UserHonorific userHonorific) => throw ReadOnly();

        private IOrgStructureRepository real => data.RealRepository;
    }

    internal IOrgStructureRepository RealRepository => real;
}

/// <summary>A unit of work that refuses to save (for the services the preview runs).</summary>
internal sealed class NoSaving : IOrgStructureUnitOfWork, IOrganizationUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw ImpactData.ReadOnly();
}
