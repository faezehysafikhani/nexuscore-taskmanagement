using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

public interface ICommitmentRepository
{
    Task<ContractMilestone?> GetMilestoneAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContractMilestone>> ListMilestonesAsync(Guid contractId, CancellationToken cancellationToken);
    Task AddMilestoneAsync(ContractMilestone milestone, CancellationToken cancellationToken);
    Task RemoveMilestoneAsync(ContractMilestone milestone, CancellationToken cancellationToken);

    Task<ContractObligation?> GetObligationAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContractObligation>> ListObligationsAsync(Guid contractId, CancellationToken cancellationToken);
    Task AddObligationAsync(ContractObligation obligation, CancellationToken cancellationToken);
    Task RemoveObligationAsync(ContractObligation obligation, CancellationToken cancellationToken);

    /// <summary>Everything a contract holds, removed together with it.</summary>
    Task RemoveAllOfContractAsync(Guid contractId, CancellationToken cancellationToken);
}

/// <summary>The milestones and obligations of a contract (what the other party, and the employer, are held to).</summary>
public interface IContractCommitmentService
{
    Task<Result<IReadOnlyList<ContractMilestoneDto>>> ListMilestonesAsync(Guid contractId, CancellationToken cancellationToken);
    Task<Result<ContractMilestoneDto>> CreateMilestoneAsync(Guid contractId, SaveContractMilestoneRequest request, CancellationToken cancellationToken);
    Task<Result<ContractMilestoneDto>> UpdateMilestoneAsync(Guid contractId, Guid id, SaveContractMilestoneRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteMilestoneAsync(Guid contractId, Guid id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ContractObligationDto>>> ListObligationsAsync(Guid contractId, CancellationToken cancellationToken);
    Task<Result<ContractObligationDto>> CreateObligationAsync(Guid contractId, SaveContractObligationRequest request, CancellationToken cancellationToken);
    Task<Result<ContractObligationDto>> UpdateObligationAsync(Guid contractId, Guid id, SaveContractObligationRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteObligationAsync(Guid contractId, Guid id, CancellationToken cancellationToken);

    Task<Result<ContractCommitmentsSummaryDto>> GetSummaryAsync(Guid contractId, CancellationToken cancellationToken);
}
