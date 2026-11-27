using Nexus.Organization.Application;
using NexusCore.Application.Identity.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <inheritdoc cref="IUnitReachScope"/>
public sealed class CompanyUnitReach(CompanyReachScope reach, IOrgStructureRepository repository, IOrganizationUnitRepository units) : IUnitReachScope
{
    public async Task<IReadOnlySet<Guid>?> GetUnitsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (await reach.GetCompaniesAsync(tenantId, userId, cancellationToken) is not { } companies)
        {
            return null;
        }

        var tops = (await repository.ListCompanyUnitsAsync(tenantId, cancellationToken))
            .Where(link => companies.Contains(link.CompanyId))
            .Select(link => link.UnitId);
        var childrenOf = (await units.ListAsync(tenantId, cancellationToken))
            .Where(unit => unit.ParentUnitId is not null)
            .ToLookup(unit => unit.ParentUnitId!.Value, unit => unit.Id);

        var visible = new HashSet<Guid>();
        var stack = new Stack<Guid>(tops);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visible.Add(id))
            {
                continue;
            }

            foreach (var child in childrenOf[id])
            {
                stack.Push(child);
            }
        }

        return visible;
    }
}
