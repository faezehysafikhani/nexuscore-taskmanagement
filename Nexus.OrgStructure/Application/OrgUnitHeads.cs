using NexusCore.Application.Identity.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <inheritdoc cref="IOrgUnitHeads"/>
public sealed class OrgUnitHeads(IOrgStructureRepository repository) : IOrgUnitHeads
{
    public async Task<IReadOnlyCollection<Guid>> UnitsHeadedByAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var posts = (await repository.ListUnitPositionsAsync(tenantId, cancellationToken)).ToDictionary(post => post.Id);
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        var held = holdings.Where(h => h.UserId == userId && h.IsActiveAt(now)).Select(h => h.UnitPositionId).ToHashSet();

        // Posts they stand in for now: named on the stand-in, or through the post they hold.
        foreach (var acting in await repository.ListPostActingsAsync(tenantId, cancellationToken))
        {
            var vacant = !holdings.Any(h => h.UnitPositionId == acting.VacantPostId && h.IsActiveAt(now));
            if (acting.IsOnAt(now, vacant) && (acting.AppliedUserId == userId || held.Contains(acting.ActingPostId)))
            {
                held.Add(acting.VacantPostId);
            }
        }

        var units = new HashSet<Guid>();
        var headedCompanies = (await repository.ListCompaniesAsync(tenantId, cancellationToken))
            .Where(company => company.ManagerUserId == userId)
            .Select(company => company.Id)
            .ToHashSet();
        foreach (var postId in held)
        {
            if (!posts.TryGetValue(postId, out var post))
            {
                continue;
            }

            if (post.IsCompanyHead)
            {
                headedCompanies.Add(post.CompanyId);
            }
            else if (post.IsManagerPost && post.UnitId is { } unitId)
            {
                units.Add(unitId);
            }
        }

        if (headedCompanies.Count > 0)
        {
            foreach (var link in await repository.ListCompanyUnitsAsync(tenantId, cancellationToken))
            {
                if (headedCompanies.Contains(link.CompanyId))
                {
                    units.Add(link.UnitId);
                }
            }
        }

        return units;
    }
}
