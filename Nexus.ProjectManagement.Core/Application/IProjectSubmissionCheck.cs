using Nexus.ProjectManagement.Core.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Application;

/// <summary>
/// How a module that holds a project's plan can say "not yet" to sending the project for approval, without Core knowing the module: each
/// installed module that has such a rule registers one. A project is sent only when no check objects; with none registered nothing blocks it.
/// </summary>
public interface IProjectSubmissionCheck
{
    /// <summary>The reason the project cannot be sent for approval (as an error with its own code), or null when this module has no objection.</summary>
    Task<Error?> CheckAsync(Project project, CancellationToken cancellationToken);
}
