using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.StakeholderManagement.Domain;

/// <summary>
/// How a project words its engagement strategy for each of the four power x interest quadrants. A null text means "the default"
/// (<see cref="StakeholderStrategyDefaults"/>); a project with no row uses the defaults for all four.
/// </summary>
public sealed class StakeholderProjectSettings : AuditableEntity<Guid>
{
    private StakeholderProjectSettings() : base(Guid.Empty)
    {
    }

    public StakeholderProjectSettings(Guid id, Guid tenantId, Guid projectId) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }

    /// <summary>High power, high interest.</summary>
    public string? ManageClosely { get; private set; }

    /// <summary>High power, low-to-medium interest.</summary>
    public string? KeepSatisfied { get; private set; }

    /// <summary>Low-to-medium power, high interest.</summary>
    public string? KeepInformed { get; private set; }

    /// <summary>Low-to-medium power, low-to-medium interest.</summary>
    public string? Monitor { get; private set; }

    public void SetStrategies(string? manageClosely, string? keepSatisfied, string? keepInformed, string? monitor)
    {
        ManageClosely = Clean(manageClosely);
        KeepSatisfied = Clean(keepSatisfied);
        KeepInformed = Clean(keepInformed);
        Monitor = Clean(monitor);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

public static class StakeholderStrategyDefaults
{
    public const string ManageClosely = "مدیریت دقیق نیازمندیها";
    public const string KeepSatisfied = "تامین نیازمندیها";
    public const string KeepInformed = "مطلع نگه داشتن";
    public const string Monitor = "حداقل تلاش برای مدیریت نیازمندیها";
}
