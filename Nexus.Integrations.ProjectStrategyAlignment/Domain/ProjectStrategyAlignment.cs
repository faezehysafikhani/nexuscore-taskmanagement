using NexusCore.SharedKernel.Domain;

// Deliberately Nexus.Integrations.StrategyAlignment, not ...ProjectStrategyAlignment - the
// entity below is named ProjectStrategyAlignment, and nesting it under an identically-named
// namespace segment breaks unqualified references to it (see RiskManagement/Risk for the full
// explanation). Physical folder/project name stays Nexus.Integrations.ProjectStrategyAlignment
// to match the target tree; only the C# namespace differs.
namespace Nexus.Integrations.StrategyAlignment.Domain;

public enum AlignmentLevel { None, Low, Medium, High }

/// <summary>
/// This entity is the ONLY thing that knows both a Project and a Strategy exist - it owns the
/// relationship, not either side. Bare Guid references, same as every other cross-module link
/// in the platform; no navigation property into either Project or Strategy.
/// </summary>
public sealed class ProjectStrategyAlignment : AuditableEntity<Guid>
{
    private ProjectStrategyAlignment() : base(Guid.Empty)
    {
    }

    public ProjectStrategyAlignment(Guid id, Guid tenantId, Guid projectId, Guid strategyId, AlignmentLevel alignmentLevel) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        StrategyId = strategyId;
        AlignmentLevel = alignmentLevel;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid StrategyId { get; private set; }
    public AlignmentLevel AlignmentLevel { get; private set; }
    public decimal? AlignmentPercentage { get; private set; }

    public void Update(AlignmentLevel alignmentLevel, decimal? alignmentPercentage)
    {
        AlignmentLevel = alignmentLevel;
        AlignmentPercentage = alignmentPercentage;
    }
}
