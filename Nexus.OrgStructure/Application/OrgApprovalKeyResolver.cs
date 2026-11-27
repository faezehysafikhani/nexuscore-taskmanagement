using Nexus.Organization.Application;
using NexusCore.Application.Approvals;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Lets a workflow step name its approver from the organization chart instead of a fixed user or role:
/// the submitter's manager (<see cref="ApprovalParticipants.OrgManager"/>) or their manager's manager
/// (<see cref="ApprovalParticipants.OrgManager2"/>), whoever holds a post (<c>Post:{id}</c>; while it is vacant,
/// its stand-in), or a position (<c>Pos:{id}</c>: the nearest holder above the submitter, else its only holder).
/// The approver is fixed when the approval starts, as for every other step.
/// </summary>
public sealed class OrgApprovalKeyResolver(
    IOrgStructureRepository repository,
    IOrganizationUnitRepository units,
    IOrgGraphProvider? graphs = null) : IApprovalKeyResolver
{
    public async Task<Guid?> ResolveAsync(ApprovalSubject subject, Guid? submitterUserId, string key, CancellationToken cancellationToken)
    {
        var tenantId = subject.TenantId;
        if (key == ApprovalParticipants.OrgManager || key == ApprovalParticipants.OrgManager2)
        {
            if (submitterUserId is not { } submitter)
            {
                return null;
            }

            var chain = (await BuildResolverAsync(tenantId, cancellationToken)).ManagementChainOf(submitter);
            var index = key == ApprovalParticipants.OrgManager ? 0 : 1;
            return chain.Count > index ? chain[index] : null;
        }

        if (key.StartsWith(ApprovalParticipants.PostPrefix, StringComparison.Ordinal)
            && Guid.TryParse(key[ApprovalParticipants.PostPrefix.Length..], out var postId))
        {
            return await HolderOfPostAsync(tenantId, postId, cancellationToken);
        }

        if (key.StartsWith(ApprovalParticipants.PositionPrefix, StringComparison.Ordinal)
            && Guid.TryParse(key[ApprovalParticipants.PositionPrefix.Length..], out var positionId))
        {
            return await HolderOfPositionAsync(tenantId, positionId, submitterUserId, cancellationToken);
        }

        return null;
    }

    private async Task<Guid?> HolderOfPostAsync(Guid tenantId, Guid postId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        if (holdings.FirstOrDefault(h => h.UnitPositionId == postId && h.IsActiveAt(now)) is { } holding)
        {
            return holding.UserId;
        }

        // Vacant: whoever stands in for it now.
        var acting = (await repository.ListPostActingsAsync(tenantId, cancellationToken))
            .FirstOrDefault(a => a.VacantPostId == postId && a.IsOnAt(now, postVacant: true));
        if (acting is null)
        {
            return null;
        }

        return acting.AppliedUserId
            ?? holdings.FirstOrDefault(h => h.UnitPositionId == acting.ActingPostId && h.IsActiveAt(now))?.UserId;
    }

    private async Task<Guid?> HolderOfPositionAsync(Guid tenantId, Guid positionId, Guid? submitterUserId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var posts = (await repository.ListUnitPositionsAsync(tenantId, cancellationToken))
            .Where(post => post.PositionId == positionId)
            .Select(post => post.Id)
            .ToHashSet();
        var holders = (await repository.ListUserPositionsAsync(tenantId, cancellationToken))
            .Where(h => posts.Contains(h.UnitPositionId) && h.IsActiveAt(now))
            .Select(h => h.UserId)
            .Distinct()
            .ToList();
        if (holders.Count == 0)
        {
            return null;
        }

        if (submitterUserId is { } submitter)
        {
            var above = (await BuildResolverAsync(tenantId, cancellationToken)).ManagementChainOf(submitter);
            if (above.FirstOrDefault(holders.Contains) is { } nearest && nearest != Guid.Empty)
            {
                return nearest;
            }
        }

        return holders.Count == 1 ? holders[0] : null;
    }

    private async Task<HierarchyResolver> BuildResolverAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var companies = await repository.ListCompaniesAsync(tenantId, cancellationToken);
        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        var seats = await repository.ListUnitPositionsAsync(tenantId, cancellationToken);
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        var postSuperiors = PostSuperiors.Of(
            assignments.Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId)), seats, holdings);
        var graph = await (graphs ?? new OrgGraphProvider(units, repository)).GetAsync(tenantId, cancellationToken);
        return new HierarchyResolver(
            graph,
            companies.ToDictionary(company => company.Id, company => company.ManagerUserId),
            assignments.Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId,
                postSuperiors.TryGetValue(a.UserId, out var superior) ? superior : null)));
    }
}
