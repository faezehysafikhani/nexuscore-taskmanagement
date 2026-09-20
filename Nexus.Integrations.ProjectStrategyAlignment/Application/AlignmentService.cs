using Nexus.Integrations.StrategyAlignment.Application.Dtos;
using Nexus.Integrations.StrategyAlignment.Domain;
using Nexus.ProjectManagement.Core.Application;
using Nexus.StrategyManagement.Application;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.StrategyAlignment.Application;

/// <summary>
/// Owns the Project x Strategy relationship. Both Project and Strategy stay entirely unaware
/// of alignment - this service is the only place that reads from both of their repositories.
/// </summary>
public sealed class AlignmentService(
    IAlignmentRepository repository,
    IProjectRepository projectRepository,
    IStrategyRepository strategyRepository,
    IStrategyAlignmentUnitOfWork unitOfWork) : IAlignmentService
{
    public async Task<Result<IReadOnlyList<ProjectStrategyAlignmentDto>>> ListAsync(Guid tenantId, Guid? projectId, Guid? strategyId, CancellationToken cancellationToken)
    {
        var alignments = await repository.ListAsync(tenantId, projectId, strategyId, cancellationToken);
        return Result.Success<IReadOnlyList<ProjectStrategyAlignmentDto>>(alignments.Select(ToDto).ToList());
    }

    public async Task<Result<ProjectStrategyAlignmentDto>> CreateAsync(CreateAlignmentRequest request, CancellationToken cancellationToken)
    {
        if (await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken) is null)
        {
            return Result.Failure<ProjectStrategyAlignmentDto>(Error.Validation("Project was not found."));
        }

        if (await strategyRepository.GetByIdAsync(request.StrategyId, cancellationToken) is null)
        {
            return Result.Failure<ProjectStrategyAlignmentDto>(Error.Validation("Strategy was not found."));
        }

        var existing = await repository.ListAsync(request.TenantId, request.ProjectId, request.StrategyId, cancellationToken);
        if (existing.Count > 0)
        {
            return Result.Failure<ProjectStrategyAlignmentDto>(Error.Conflict("This project and strategy are already linked - update the existing alignment instead."));
        }

        var alignment = new ProjectStrategyAlignment(Guid.NewGuid(), request.TenantId, request.ProjectId, request.StrategyId, request.AlignmentLevel);
        alignment.Update(request.AlignmentLevel, request.AlignmentPercentage);

        await repository.AddAsync(alignment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(alignment));
    }

    public async Task<Result<ProjectStrategyAlignmentDto>> UpdateAsync(Guid id, UpdateAlignmentRequest request, CancellationToken cancellationToken)
    {
        var alignment = await repository.GetByIdAsync(id, cancellationToken);
        if (alignment is null)
        {
            return Result.Failure<ProjectStrategyAlignmentDto>(Error.NotFound("Alignment not found."));
        }

        alignment.Update(request.AlignmentLevel, request.AlignmentPercentage);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(alignment));
    }

    private static ProjectStrategyAlignmentDto ToDto(ProjectStrategyAlignment alignment) =>
        new(alignment.Id, alignment.TenantId, alignment.ProjectId, alignment.StrategyId, alignment.AlignmentLevel, alignment.AlignmentPercentage);
}
