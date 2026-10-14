using Nexus.Integrations.StrategyAlignment.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.StrategyAlignment.Application;

public interface IAlignmentService
{
    Task<Result<IReadOnlyList<ProjectStrategyAlignmentDto>>> ListAsync(Guid tenantId, Guid? projectId, Guid? strategyId, CancellationToken cancellationToken);
    Task<Result<ProjectStrategyAlignmentDto>> CreateAsync(CreateAlignmentRequest request, CancellationToken cancellationToken);
    Task<Result<ProjectStrategyAlignmentDto>> UpdateAsync(Guid id, UpdateAlignmentRequest request, CancellationToken cancellationToken);

    /// <summary>Default interface members, so another implementation of this interface keeps compiling.</summary>
    Task<Result<IReadOnlyList<ProjectAlignmentScoreDto>>> GetScoresAsync(Guid tenantId, ProjectAlignmentScoresRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<IReadOnlyList<ProjectAlignmentScoreDto>>(Error.NotFound("Not supported.")));

    Task<Result<ProjectAlignmentScoreDto>> SetLevelsAsync(SetProjectAlignmentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<ProjectAlignmentScoreDto>(Error.NotFound("Not supported.")));
}
