namespace Nexus.ProjectManagement.Core.Application;

/// <summary>
/// Which of the ways to see another person's work are switched on for an organisation. Every switch is on by
/// default, which is exactly how visibility worked before the switches existed. An optional extension point:
/// with no <see cref="IVisibilityRules"/> registered, everything is on.
/// </summary>
public sealed record VisibilityRules(
    bool ViewByOwner = true,
    bool ViewByManagerOrResponsible = true,
    bool ViewByApprovers = true,
    bool ViewByResources = true,
    bool ViewByOrgHeads = true)
{
    public static VisibilityRules AllOn { get; } = new();
}

public interface IVisibilityRules
{
    Task<VisibilityRules> GetAsync(Guid tenantId, CancellationToken cancellationToken);
}
