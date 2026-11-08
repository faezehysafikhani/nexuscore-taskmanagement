namespace Nexus.ProjectManagement.Core.Application;

/// <summary>Opt-in behaviour of the Project module. Every switch defaults to off, so a project that
/// already uses the module sees exactly what it always saw.</summary>
public sealed class ProjectOptions
{
    /// <summary>When on, creating or updating a project requires a complete charter (manager, owning unit, both dates,
    /// goal and requirements) and refuses charter text that is meaningless (shorter than four characters, or
    /// placeholder words such as "test"). Off (the default) accepts whatever the earlier rules accepted.</summary>
    public bool ValidateCharter { get; set; }

    /// <summary>When on, two projects of a tenant cannot share a name (compared ignoring case). Off (the default) allows it, as before.</summary>
    public bool RequireUniqueName { get; set; }
}
