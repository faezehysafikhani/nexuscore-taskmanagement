using Nexus.StrategyManagement.Application.Dtos;
using Nexus.StrategyManagement.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.StrategyManagement.Application;

public sealed class StrategyService(
    IStrategyRepository repository,
    IStrategyUnitOfWork unitOfWork) : IStrategyService
{
    public async Task<Result<IReadOnlyList<StrategyDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var strategies = await repository.ListAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<StrategyDto>>(strategies.Select(ToDto).ToList());
    }

    public async Task<Result<StrategyDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var strategy = await repository.GetByIdAsync(id, cancellationToken);
        return strategy is null
            ? Result.Failure<StrategyDto>(Error.NotFound("Strategy not found."))
            : Result.Success(ToDto(strategy));
    }

    public async Task<Result<StrategyDto>> CreateAsync(CreateStrategyRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<StrategyDto>(Error.Validation("Name is required."));
        }

        if (request.ParentStrategyId is { } parentId && await repository.GetByIdAsync(parentId, cancellationToken) is null)
        {
            return Result.Failure<StrategyDto>(Error.Validation("Parent strategy was not found."));
        }

        var strategy = new Strategy(Guid.NewGuid(), request.TenantId, request.Name, request.ParentStrategyId);
        strategy.UpdateDetails(request.Name, request.Description, request.Weight, request.ParentStrategyId);

        await repository.AddAsync(strategy, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(strategy));
    }

    public async Task<Result<StrategyDto>> UpdateAsync(Guid id, UpdateStrategyRequest request, CancellationToken cancellationToken)
    {
        var strategy = await repository.GetByIdAsync(id, cancellationToken);
        if (strategy is null)
        {
            return Result.Failure<StrategyDto>(Error.NotFound("Strategy not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<StrategyDto>(Error.Validation("Name is required."));
        }

        if (request.ParentStrategyId == id)
        {
            return Result.Failure<StrategyDto>(Error.Validation("A strategy cannot be its own parent."));
        }

        strategy.UpdateDetails(request.Name, request.Description, request.Weight, request.ParentStrategyId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(strategy));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var strategy = await repository.GetByIdAsync(id, cancellationToken);
        if (strategy is null)
        {
            return Result.Failure(Error.NotFound("Strategy not found."));
        }

        var all = await repository.ListAsync(strategy.TenantId, cancellationToken);
        if (all.Any(s => s.ParentStrategyId == id))
        {
            return Result.Failure(Error.Conflict("Delete or reassign the sub-strategies first."));
        }

        await repository.RemoveAsync(strategy, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static StrategyDto ToDto(Strategy strategy) =>
        new(strategy.Id, strategy.TenantId, strategy.Name, strategy.Description, strategy.Weight, strategy.ParentStrategyId);
}
