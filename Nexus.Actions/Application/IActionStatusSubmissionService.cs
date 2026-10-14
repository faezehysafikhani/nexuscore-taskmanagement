using Nexus.Actions.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Actions.Application;

public interface IActionStatusSubmissionService
{
    /// <summary>Every status request of the tenant, or of one project's actions; newest first.</summary>
    Task<Result<IReadOnlyList<ActionStatusSubmissionDto>>> ListAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ActionStatusSubmissionDto>>> ListByActionAsync(Guid actionId, CancellationToken cancellationToken);
    Task<Result<ActionStatusSubmissionDto>> SubmitAsync(SubmitActionStatusRequest request, CancellationToken cancellationToken);
}
