using Nexus.Organization.Application;
using NexusCore.Application.Identity.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>The management chain of a person from the structure (their managers, nearest first); empty for someone with no place in it.</summary>
public sealed class HierarchyManagementChainProvider(
    IOrgStructureRepository repository,
    IOrganizationUnitRepository units,
    IOrgGraphProvider? graphs = null) : IManagementChainProvider
{
    public async Task<IReadOnlyList<Guid>> GetChainAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var companies = await repository.ListCompaniesAsync(tenantId, cancellationToken);
        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        var seatList = await repository.ListUnitPositionsAsync(tenantId, cancellationToken);
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        var postSuperiors = PostSuperiors.Of(
            assignments.Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId)), seatList, holdings);
        var graph = await (graphs ?? new OrgGraphProvider(units, repository)).GetAsync(tenantId, cancellationToken);
        var resolver = new HierarchyResolver(
            graph,
            companies.ToDictionary(company => company.Id, company => company.ManagerUserId),
            assignments.Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId, postSuperiors.TryGetValue(a.UserId, out var superior) ? superior : null)));
        return resolver.ManagementChainOf(userId);
    }
}
