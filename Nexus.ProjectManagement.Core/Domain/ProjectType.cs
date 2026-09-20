namespace Nexus.ProjectManagement.Core.Domain;

/// <summary>
/// Project Core only classifies the project - it does not implement Waterfall or Agile
/// planning itself. See Nexus.ProjectManagement.Waterfall / .Agile.
/// </summary>
public enum ProjectType
{
    Waterfall,
    Agile
}
