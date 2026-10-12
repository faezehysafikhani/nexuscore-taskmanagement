using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.RiskManagement.Domain;

/// <summary>
/// What a project says about its risks beyond the risks themselves: the wording of the low / medium / high bands of probability
/// and impact, and the colour of each probability x impact cell. Both are kept as typed JSON (see RiskSettingsJson) so the
/// shape can grow without a new column each time; a project with no row uses the defaults.
/// </summary>
public sealed class RiskProjectSettings : AuditableEntity<Guid>
{
    private RiskProjectSettings() : base(Guid.Empty)
    {
    }

    public RiskProjectSettings(Guid id, Guid tenantId, Guid projectId) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }

    /// <summary>JSON array of level definitions, or null = the defaults.</summary>
    public string? DefinitionsJson { get; private set; }

    /// <summary>JSON array of matrix cell colours, or null = the defaults.</summary>
    public string? MatrixColorsJson { get; private set; }

    public void SetDefinitions(string? json) => DefinitionsJson = json;

    public void SetMatrixColors(string? json) => MatrixColorsJson = json;
}
