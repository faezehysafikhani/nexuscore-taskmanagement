using Nexus.Organization.Domain;

namespace Nexus.CompositionTests;

internal static class OrgUnits
{
    /// <summary>A unit with its code and its place in the chart, as the organization structure sets them.</summary>
    public static OrganizationUnit Make(Guid id, Guid tenantId, string name, string? code = null, Guid? parentId = null, Guid? managerUserId = null)
    {
        var unit = new OrganizationUnit(id, tenantId, name, managerUserId);
        unit.SetNodeCode(code);
        unit.SetParent(parentId);
        return unit;
    }
}
