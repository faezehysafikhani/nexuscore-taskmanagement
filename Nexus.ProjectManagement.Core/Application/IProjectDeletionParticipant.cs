namespace Nexus.ProjectManagement.Core.Application;

/// <summary>
/// How a module that keeps data about a project takes part in deleting it, without Core knowing
/// the module: each installed module (or the integration that joins it to Core) registers one.
/// A project is deleted only when no participant objects; afterwards each gets to clean up what is
/// purely its own bookkeeping about the project. With no participant registered nothing blocks a delete.
/// </summary>
public interface IProjectDeletionParticipant
{
    /// <summary>Why the project cannot be deleted (what still depends on it, as readable text), or null when this module has no objection.</summary>
    Task<string?> GetBlockingReasonAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken);

    /// <summary>Called once the project is gone. Remove only what means nothing without it (a team list, say); the default does nothing.</summary>
    Task OnProjectDeletedAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken) => Task.CompletedTask;
}
