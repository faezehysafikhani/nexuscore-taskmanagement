using NexusCore.SharedKernel.Domain;

// Deliberately Nexus.StrategyManagement, not Nexus.Strategy - see
// Nexus.ProjectManagement.RiskManagement.Domain.Risk for why nesting a type under an
// identically-named namespace segment breaks unqualified references to it. Physical
// folder/project name stays Nexus.Strategy to match the target tree.
namespace Nexus.StrategyManagement.Domain;

public sealed class Strategy : AuditableEntity<Guid>
{
    private Strategy() : base(Guid.Empty)
    {
        Name = string.Empty;
    }

    public Strategy(Guid id, Guid tenantId, string name, Guid? parentStrategyId = null) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        ParentStrategyId = parentStrategyId;
        Weight = 0;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public decimal Weight { get; private set; }
    public Guid? ParentStrategyId { get; private set; }

    /// <summary>Where the strategy stands among its siblings when the tree is shown; ties (and every strategy created before this
    /// existed, all 0) fall back to the name.</summary>
    public int SortOrder { get; private set; }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void UpdateDetails(string name, string? description, decimal weight, Guid? parentStrategyId)
    {
        Name = name.Trim();
        Description = description;
        Weight = weight;
        ParentStrategyId = parentStrategyId;
    }
}
