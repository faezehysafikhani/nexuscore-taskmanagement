namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// The organisation units a person heads in the organization chart: those whose manager post they hold
/// (or stand in for), and the top units of a company they head. Installed by the organization-structure
/// module; without it nobody heads a unit through the chart.
/// </summary>
public interface IOrgUnitHeads
{
    Task<IReadOnlyCollection<Guid>> UnitsHeadedByAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}
