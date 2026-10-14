using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.RiskManagement.Application;

public interface IRiskResponseSubmissionService
{
    Task<Result<IReadOnlyList<RiskResponseSubmissionDto>>> ListForProjectsAsync(ListRisksForProjectsRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<RiskResponseSubmissionDto>>> ListByRiskAsync(Guid riskId, CancellationToken cancellationToken);
    Task<Result<RiskResponseSubmissionDto>> SubmitAsync(SubmitRiskResponseRequest request, CancellationToken cancellationToken);
}
