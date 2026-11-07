using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Interfaces;

namespace Nexus.OrgStructure.Infrastructure;

/// <summary>
/// The permissions a user gets from the posts they hold now (the position's and the post's own), and,
/// while a stand-in (جانشین) is on, from the post they stand in for. Several posts: all of them
/// together. Nothing comes from the position above, from a post not held yet or already left, or from
/// an inactive position. It is asked on every request, so an appointment that starts or ends on a set
/// day changes access by itself on that day.
/// </summary>
public sealed class PositionPermissionProvider(OrgStructureDbContext dbContext, TimeProvider? clock = null) : IPositionPermissionProvider
{
    public async Task<IReadOnlyList<string>> GetPermissionNamesAsync(Guid userId, CancellationToken cancellationToken) =>
        (await GrantsAsync(userId, cancellationToken)).Select(grant => grant.Name).Distinct(StringComparer.Ordinal).ToList();

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetGrantingPositionsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await GrantsAsync(userId, cancellationToken))
            .GroupBy(grant => grant.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(grant => grant.Label).Distinct().OrderBy(label => label).ToList(), StringComparer.Ordinal);

    private sealed record Grant(string Name, string Label);

    private async Task<List<Grant>> GrantsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();

        // The posts held now. Not filtered by organization: the user id alone names them.
        var held = await dbContext.UserPositions.IgnoreQueryFilters()
            .Where(x => x.UserId == userId && (x.StartDate == null || x.StartDate <= now) && (x.EndDate == null || now < x.EndDate))
            .Select(x => x.UnitPositionId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (held.Count == 0)
        {
            return [];
        }

        // Posts stood in for: a dated stand-in in its time, an undated one while the vacant post has no holder.
        var standIns = await dbContext.PostActings.IgnoreQueryFilters()
            .Where(x => held.Contains(x.ActingPostId))
            .ToListAsync(cancellationToken);
        var undated = standIns.Where(x => !x.IsDated).Select(x => x.VacantPostId).Distinct().ToList();
        var occupied = undated.Count == 0
            ? new HashSet<Guid>()
            : (await dbContext.UserPositions.IgnoreQueryFilters()
                .Where(x => undated.Contains(x.UnitPositionId) && (x.StartDate == null || x.StartDate <= now) && (x.EndDate == null || now < x.EndDate))
                .Select(x => x.UnitPositionId)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();
        var standingFor = standIns
            .Where(x => x.IsOnAt(now, postVacant: !occupied.Contains(x.VacantPostId)))
            .Select(x => x.VacantPostId)
            .Distinct()
            .Except(held)
            .ToList();

        var postIds = held.Concat(standingFor).ToList();
        var posts = await dbContext.UnitPositions.IgnoreQueryFilters()
            .Where(x => postIds.Contains(x.Id))
            .Join(dbContext.Positions.IgnoreQueryFilters().Where(p => p.IsActive), x => x.PositionId, p => p.Id,
                (x, p) => new { PostId = x.Id, x.PositionId, p.Title, x.Code })
            .ToListAsync(cancellationToken);
        if (posts.Count == 0)
        {
            return [];
        }

        var positionIds = posts.Select(x => x.PositionId).Distinct().ToList();
        var activePostIds = posts.Select(x => x.PostId).ToList();
        var byPosition = await dbContext.PositionPermissions.IgnoreQueryFilters()
            .Where(x => positionIds.Contains(x.PositionId))
            .Select(x => new { x.PositionId, x.PermissionName })
            .ToListAsync(cancellationToken);
        var byPost = await dbContext.UnitPositionPermissions.IgnoreQueryFilters()
            .Where(x => activePostIds.Contains(x.UnitPositionId))
            .Select(x => new { x.UnitPositionId, x.PermissionName })
            .ToListAsync(cancellationToken);

        var grants = new List<Grant>();
        foreach (var post in posts)
        {
            var prefix = standingFor.Contains(post.PostId) ? "جانشین " : string.Empty;
            var label = $"{prefix}{post.Title}" + (string.IsNullOrWhiteSpace(post.Code) ? string.Empty : $" ({post.Code})");
            grants.AddRange(byPosition.Where(x => x.PositionId == post.PositionId).Select(x => new Grant(x.PermissionName, label)));
            grants.AddRange(byPost.Where(x => x.UnitPositionId == post.PostId).Select(x => new Grant(x.PermissionName, label)));
        }

        return grants;
    }
}
