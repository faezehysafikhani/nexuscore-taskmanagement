namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// The organisation units a person may see in lists and pickers: where the organization structure keeps everyone to
/// their own company, the units of that company (with everything under them). Null when not limited. Installed by the
/// organization-structure module; without it every unit is listed.
/// </summary>
public interface IUnitReachScope
{
    Task<IReadOnlySet<Guid>?> GetUnitsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}
