using Nexus.ProjectManagement.StakeholderManagement.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.StakeholderManagement.Application;

public interface IStakeholderService
{
    Task<Result<IReadOnlyList<StakeholderDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Result<StakeholderDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<StakeholderDto>> CreateAsync(CreateStakeholderRequest request, CancellationToken cancellationToken);
    Task<Result<StakeholderDto>> UpdateAsync(Guid id, UpdateStakeholderRequest request, CancellationToken cancellationToken);
    Task<Result<StakeholderDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken);
}
