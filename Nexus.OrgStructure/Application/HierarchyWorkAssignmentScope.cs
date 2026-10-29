using Microsoft.Extensions.Options;
using Nexus.Organization.Application;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Application;

public sealed class OrgStructureOptions
{
    public const string SectionName = "OrgStructure";

    /// <summary>
    /// Whether people make responsible for tasks only those at their own level or below
    /// (<see cref="HierarchyWorkAssignmentScope"/>). Off unless the product turns it on.
    /// </summary>
    public bool LimitTaskAssignment { get; set; }

    /// <summary>
    /// Whether tasks are given to posts instead of people (<see cref="TaskPostDirectory"/>): a task
    /// moves with its posts to their new holders. Off unless the product turns it on.
    /// </summary>
    public bool AssignTasksToPosts { get; set; }

    /// <summary>
    /// Whether everyone is kept to their own company (<see cref="CompanyReachScope"/>): they see,
    /// message, add to teams and give tasks only to its people, and see only its structure.
    /// Holders of OrgStructurePermissions.AllCompanies are not limited. Off unless the product turns it on.
    /// </summary>
    public bool LimitToOwnCompany { get; set; }

    /// <summary>
    /// Temporary (until the dynamic structure is delivered): whether the node API may put a node
    /// under another node. Off: nodes are only made and moved at the top of their company, as
    /// before. Turned on only where the nested structure is being tried out.
    /// </summary>
    public bool AllowNesting { get; set; }
}

/// <summary>
/// With <see cref="OrgStructureOptions.LimitTaskAssignment"/> on, a user makes responsible for a
/// task only: themselves, the people below them (their subordinates, at any depth), the people of
/// their own deputy at their level or lower, and the people of their own company at their own level
/// (e.g. an expert of another deputy) - except, in their own deputy, someone at their level in a
/// position above theirs (the deputy's positions are in rank order). Never anyone above them.
///
/// A person's level is that of their position: how many superior positions («سمت مافوق») are
/// above it, up to the head's (the highest of the positions they hold). Someone with no position
/// is measured by how many managers they have above them in the chart instead.
///
/// Not limited: a user with no place in the structure, and holders of
/// <see cref="OrgStructurePermissions.AssignTasksToAnyLevel"/>. Anyone with no place yet may be
/// made responsible by anyone. Only people newly made responsible are checked.
/// </summary>
public sealed class HierarchyWorkAssignmentScope(
    IOrgStructureRepository repository,
    IOrganizationUnitRepository units,
    ICurrentUserContext currentUser,
    IOptions<OrgStructureOptions> options,
    IOrgGraphProvider? graphs = null) : IWorkAssignmentScope
{
    public async Task<WorkAssignmentLimit?> GetLimitAsync(Guid tenantId, Guid assignerUserId, CancellationToken cancellationToken)
    {
        if (!options.Value.LimitTaskAssignment
            || (currentUser.UserId == assignerUserId && currentUser.HasPermission(OrgStructurePermissions.AssignTasksToAnyLevel)))
        {
            return null;
        }

        var companies = await repository.ListCompaniesAsync(tenantId, cancellationToken);
        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        var heads = companies.Where(c => c.ManagerUserId is not null).Select(c => c.ManagerUserId!.Value).ToHashSet();
        var placed = assignments.Select(a => a.UserId).Concat(heads).ToHashSet();
        if (!placed.Contains(assignerUserId))
        {
            return null;
        }

        var seatList = await repository.ListUnitPositionsAsync(tenantId, cancellationToken);
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        var postSuperiors = PostSuperiors.Of(
            assignments.Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId)), seatList, holdings);
        var graph = await (graphs ?? new OrgGraphProvider(units, repository)).GetAsync(tenantId, cancellationToken);
        var resolver = new HierarchyResolver(
            graph,
            companies.ToDictionary(company => company.Id, company => company.ManagerUserId),
            assignments.Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId, postSuperiors.TryGetValue(a.UserId, out var superior) ? superior : null)));

        var levels = new WorkAssignmentLevels(
            assignments, companies, seatList, holdings,
            (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToDictionary(position => position.Id),
            resolver);
        return new WorkAssignmentLimit(placed.Where(userId => !levels.IsAllowed(assignerUserId, userId)).ToHashSet());
    }
}

/// <summary>
/// The levels behind <see cref="HierarchyWorkAssignmentScope"/>, apart from the data loading so
/// they can be tested on their own: a person's level, their rank in their own deputy, whether two
/// people are in the same deputy, and whether one may give the other work.
/// </summary>
public sealed class WorkAssignmentLevels
{
    private readonly IReadOnlyList<Domain.UserPosition> _holdings;
    private readonly IReadOnlyDictionary<Guid, Domain.UnitPosition> _seats;
    private readonly IReadOnlyDictionary<Guid, Domain.Position> _positions;
    private readonly IReadOnlyDictionary<Guid, Guid?> _unitOf;
    private readonly IReadOnlyDictionary<Guid, Guid?> _mainPositionOf;
    private readonly IReadOnlyDictionary<Guid, HashSet<Guid>> _companiesOf;
    private readonly HierarchyResolver _resolver;
    private readonly Dictionary<Guid, HashSet<Guid>> _below = [];
    private readonly Dictionary<Guid, int> _levels = [];

    public WorkAssignmentLevels(
        IReadOnlyList<Domain.UserAssignment> assignments,
        IReadOnlyList<Domain.Company> companies,
        IReadOnlyList<Domain.UnitPosition> seats,
        IReadOnlyList<Domain.UserPosition> holdings,
        IReadOnlyDictionary<Guid, Domain.Position> positions,
        HierarchyResolver resolver)
    {
        _holdings = holdings;
        _seats = seats.ToDictionary(seat => seat.Id);
        _positions = positions;
        _resolver = resolver;
        _unitOf = assignments.ToDictionary(a => a.UserId, a => a.UnitId);
        _mainPositionOf = assignments.ToDictionary(a => a.UserId, a => a.PositionId);

        // The companies a person is in: where they are placed, and those they head.
        var companiesOf = assignments.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.Select(a => a.CompanyId).ToHashSet());
        foreach (var company in companies.Where(c => c.ManagerUserId is not null))
        {
            if (!companiesOf.TryGetValue(company.ManagerUserId!.Value, out var set))
            {
                companiesOf[company.ManagerUserId.Value] = set = [];
            }

            set.Add(company.Id);
        }

        _companiesOf = companiesOf;
    }

    /// <summary>A person's rank in their own deputy: the highest of the deputy's positions they hold.</summary>
    public int? RankOf(Guid userId) => _unitOf.GetValueOrDefault(userId) is { } unitId
        ? _holdings.Where(h => h.UserId == userId && _seats.TryGetValue(h.UnitPositionId, out var seat) && seat.UnitId == unitId)
            .Select(h => (int?)_seats[h.UnitPositionId].SortOrder).Min()
        : null;

    /// <summary>How many superior positions («سمت مافوق») are above the position, up to the head's.</summary>
    public int? DepthOf(Guid positionId)
    {
        if (!_positions.ContainsKey(positionId))
        {
            return null;
        }

        var depth = 0;
        var visited = new HashSet<Guid> { positionId };
        for (var parent = _positions[positionId].ParentPositionId;
             parent is { } parentId && _positions.TryGetValue(parentId, out var above) && visited.Add(parentId);
             parent = above.ParentPositionId)
        {
            depth++;
        }

        return depth;
    }

    /// <summary>A person's level: the highest of their positions (those they hold and their main one); with none, how many managers are above them.</summary>
    public int LevelOf(Guid userId)
    {
        if (_levels.TryGetValue(userId, out var known))
        {
            return known;
        }

        return _levels[userId] = ComputeLevelOf(userId);
    }

    private int ComputeLevelOf(Guid userId)
    {
        var held = _holdings.Where(h => h.UserId == userId && _seats.ContainsKey(h.UnitPositionId))
            .Select(h => _seats[h.UnitPositionId].PositionId)
            .ToList();
        if (_mainPositionOf.GetValueOrDefault(userId) is { } main)
        {
            held.Add(main);
        }

        var depths = held.Select(DepthOf).OfType<int>().ToList();
        return depths.Count > 0 ? depths.Min() : _resolver.ManagementChainOf(userId).Count;
    }

    /// <summary>The domain («the same deputy») of a person's unit; null when they have no unit.</summary>
    public Guid? DomainOfUser(Guid userId) => _unitOf.GetValueOrDefault(userId) is { } unit ? _resolver.Graph.DomainOf(unit) : null;

    /// <summary>Both are in a unit and their units have the same domain.</summary>
    public bool InSameDeputy(Guid userId, Guid otherUserId) => DomainOfUser(userId) is { } domain && DomainOfUser(otherUserId) == domain;

    /// <summary>Whether <paramref name="assignerUserId"/> may make <paramref name="userId"/> responsible: themselves, their subordinates, and (in their own company) people at their level or lower - lower only in their own deputy - never above them.</summary>
    public bool IsAllowed(Guid assignerUserId, Guid userId)
    {
        if (!_below.TryGetValue(assignerUserId, out var below))
        {
            _below[assignerUserId] = below = _resolver.AllSubordinatesOf(assignerUserId).Select(s => s.UserId).ToHashSet();
        }

        if (userId == assignerUserId || below.Contains(userId))
        {
            return true;
        }

        var assignerCompanies = _companiesOf.GetValueOrDefault(assignerUserId) ?? [];
        var level = LevelOf(assignerUserId);

        // Only within one's own company: other companies' people are not peers.
        var theirs = LevelOf(userId);
        if (theirs < level || !(_companiesOf.GetValueOrDefault(userId)?.Overlaps(assignerCompanies) ?? false))
        {
            return false;
        }

        // Lower: within one's own deputy (another deputy's lower people get work from their own managers).
        var sameDeputy = InSameDeputy(assignerUserId, userId);
        if (theirs > level)
        {
            return sameDeputy;
        }

        // Same level - but not someone in a higher position of the same deputy.
        return !sameDeputy || RankOf(userId) is not { } rank || RankOf(assignerUserId) is not { } own || rank >= own;
    }
}
