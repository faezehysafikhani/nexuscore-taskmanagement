using Nexus.StrategyManagement.Application.Dtos;
using Nexus.StrategyManagement.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.StrategyManagement.Application;

public sealed class StrategyService(
    IStrategyRepository repository,
    IStrategyUnitOfWork unitOfWork) : IStrategyService
{
    public const string WeightsNot100ErrorCode = "strategy.weights_not_100";


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

    public async Task<Result<StrategyTreeDto>> GetTreeAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Result.Success(StrategyTree.Build(await repository.ListAsync(tenantId, cancellationToken)));

    /// <summary>
    /// Saves the whole tree in one go. Existing strategies (matched by id) are updated, new ones created, the ones left out deleted. The weights
    /// of the top-level strategies - a strategy with sub-strategies weighs what they add up to - must add up to 100 (a tree with nothing in it is fine).
    /// </summary>
    public async Task<Result<StrategyTreeDto>> SaveTreeAsync(SaveStrategyTreeRequest request, CancellationToken cancellationToken)
    {
        var items = request.Items;
        if (items.Any(item => string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Name)))
        {
            return Result.Failure<StrategyTreeDto>(Error.Validation("Every strategy needs a name."));
        }

        if (items.Select(item => item.Key).Distinct().Count() != items.Count)
        {
            return Result.Failure<StrategyTreeDto>(Error.Validation("A strategy appears twice in the tree."));
        }

        if (items.Any(item => item.Weight is < 0 or > 100))
        {
            return Result.Failure<StrategyTreeDto>(Error.Validation("A weight must be between 0 and 100."));
        }

        var keys = items.Select(item => item.Key).ToHashSet();
        if (items.Any(item => item.ParentKey is not null && (!keys.Contains(item.ParentKey) || item.ParentKey == item.Key)))
        {
            return Result.Failure<StrategyTreeDto>(Error.Validation("A strategy's parent must be another strategy of the tree."));
        }

        var parentOf = items.ToDictionary(item => item.Key, item => item.ParentKey);
        foreach (var item in items)
        {
            var seen = new HashSet<string> { item.Key };
            for (var up = item.ParentKey; up is not null; up = parentOf[up])
            {
                if (!seen.Add(up))
                {
                    return Result.Failure<StrategyTreeDto>(Error.Validation("A strategy cannot sit under itself or one of its own sub-strategies."));
                }
            }
        }

        // Checked before anything is touched, so a refused tree leaves nothing half-saved in the unit of work.
        if (items.Count > 0 && Math.Round(TotalWeight(items)) != 100)
        {
            return Result.Failure<StrategyTreeDto>(new Error(WeightsNot100ErrorCode, "The weights of the top-level strategies must add up to 100."));
        }

        var existing = (await repository.ListAsync(request.TenantId, cancellationToken)).ToDictionary(s => s.Id);
        var idOf = items.ToDictionary(
            item => item.Key,
            item => Guid.TryParse(item.Key, out var id) && existing.ContainsKey(id) ? id : Guid.NewGuid());

        var saved = new List<Strategy>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var id = idOf[item.Key];
            var parent = item.ParentKey is null ? (Guid?)null : idOf[item.ParentKey];
            if (!existing.TryGetValue(id, out var strategy))
            {
                strategy = new Strategy(id, request.TenantId, item.Name, parent);
                await repository.AddAsync(strategy, cancellationToken);
            }

            strategy.UpdateDetails(item.Name, item.Description, item.Weight, parent);
            strategy.SetSortOrder(i);
            saved.Add(strategy);
        }

        var tree = StrategyTree.Build(saved);
        foreach (var removed in existing.Values.Where(s => !idOf.Values.Contains(s.Id)))
        {
            await repository.RemoveAsync(removed, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(tree);
    }

    /// <summary>What the top-level strategies of a tree being saved weigh together (a strategy with sub-strategies weighs what they add up to).</summary>
    private static decimal TotalWeight(IReadOnlyList<SaveStrategyTreeItem> items)
    {
        var children = items.Where(i => i.ParentKey is not null).GroupBy(i => i.ParentKey!).ToDictionary(g => g.Key, g => g.ToList());
        decimal Weigh(SaveStrategyTreeItem item) => children.TryGetValue(item.Key, out var kids) ? kids.Sum(Weigh) : item.Weight;
        return items.Where(i => i.ParentKey is null).Sum(Weigh);
    }

    private static StrategyDto ToDto(Strategy strategy) =>
        new(strategy.Id, strategy.TenantId, strategy.Name, strategy.Description, strategy.Weight, strategy.ParentStrategyId);
}
