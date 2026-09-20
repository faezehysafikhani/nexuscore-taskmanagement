using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.RiskManagement.Application;

public interface IRiskService
{
    Task<Result<IReadOnlyList<RiskDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<RiskDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<RiskDto>> CreateAsync(CreateRiskRequest request, CancellationToken cancellationToken);
    Task<Result<RiskDto>> UpdateAsync(Guid id, UpdateRiskRequest request, CancellationToken cancellationToken);
    Task<Result<RiskDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
