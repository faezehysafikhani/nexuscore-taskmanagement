using Microsoft.Extensions.Options;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// With <see cref="OrgStructureOptions.LimitToOwnCompany"/> on, the signed-in user reaches only
/// the people of their own companies - the companies they are placed in and those they head -
/// and themselves. A user with no place in any company reaches no one else. Holders of
/// <see cref="OrgStructurePermissions.AllCompanies"/> are not limited.
///
/// Worked out once per request (it is scoped), as several checks of one request ask for it.
/// </summary>
public sealed class CompanyReachScope(
    IOrgStructureRepository repository,
    ICurrentUserContext currentUser,
    IOptions<OrgStructureOptions> options) : IUserReachScope
{
    private (Guid TenantId, Guid UserId, CompanyReach? Reach)? _cached;

    public async Task<UserReach?> GetReachAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        (await GetAsync(tenantId, userId, cancellationToken))?.People;

    /// <summary>The companies the user may see, or null when not limited.</summary>
    public async Task<IReadOnlySet<Guid>?> GetCompaniesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        (await GetAsync(tenantId, userId, cancellationToken))?.CompanyIds;

    /// <summary>Whether the signed-in user is limited at all, without reading the structure.</summary>
    public bool AppliesTo(Guid userId) =>
        options.Value.LimitToOwnCompany
        && !(currentUser.UserId == userId && currentUser.HasPermission(OrgStructurePermissions.AllCompanies));

    private async Task<CompanyReach?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (!AppliesTo(userId))
        {
            return null;
        }

        if (_cached is { } cached && cached.TenantId == tenantId && cached.UserId == userId)
        {
            return cached.Reach;
        }

        var companiesOf = await CompaniesOfPeopleAsync(repository, tenantId, cancellationToken);
        var mine = companiesOf.GetValueOrDefault(userId) ?? [];
        var reachable = new HashSet<Guid> { userId };
        var elsewhere = new HashSet<Guid>();
        foreach (var (person, companies) in companiesOf)
        {
            (companies.Overlaps(mine) ? reachable : elsewhere).Add(person);
        }

        elsewhere.ExceptWith(reachable);
        var reach = new CompanyReach(mine, new UserReach(reachable, elsewhere));
        _cached = (tenantId, userId, reach);
        return reach;
    }

    /// <summary>The companies each placed person is in: where they are placed, and those they head.</summary>
    internal static async Task<Dictionary<Guid, HashSet<Guid>>> CompaniesOfPeopleAsync(
        IOrgStructureRepository repository, Guid tenantId, CancellationToken cancellationToken)
    {
        var companiesOf = (await repository.ListAssignmentsAsync(tenantId, cancellationToken))
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.CompanyId).ToHashSet());
        foreach (var company in await repository.ListCompaniesAsync(tenantId, cancellationToken))
        {
            if (company.ManagerUserId is not { } head)
            {
                continue;
            }

            if (!companiesOf.TryGetValue(head, out var set))
            {
                companiesOf[head] = set = [];
            }

            set.Add(company.Id);
        }

        return companiesOf;
    }

    private sealed record CompanyReach(IReadOnlySet<Guid> CompanyIds, UserReach People);
}
