using Nexus.ProjectManagement.Delegation.Application.Dtos;
using Nexus.ProjectManagement.Delegation.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Delegation.Application;

public interface IItemDelegationUnitOfWork : IUnitOfWork;

public interface IItemDelegationRepository
{
    Task<ItemDelegation?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Delegations the user handed out or received, newest first (active ones only unless asked).</summary>
    Task<IReadOnlyList<ItemDelegation>> ListForUserAsync(Guid tenantId, Guid userId, bool activeOnly, CancellationToken cancellationToken);

    /// <summary>Every active delegation of one item.</summary>
    Task<IReadOnlyList<ItemDelegation>> ListActiveForItemAsync(Guid tenantId, DelegatedItemType itemType, Guid itemId, CancellationToken cancellationToken);

    Task AddAsync(ItemDelegation delegation, CancellationToken cancellationToken);
}

public interface IItemDelegationService
{
    Task<Result<IReadOnlyList<ItemDelegationDto>>> ListMineAsync(Guid tenantId, Guid userId, bool activeOnly, CancellationToken cancellationToken);
    Task<Result<ItemDelegationDto>> DelegateAsync(Guid tenantId, Guid callerUserId, DelegateItemRequest request, CancellationToken cancellationToken);

    /// <summary>Ends a hand-over, and with it every hand-over made of that item after it. Only the one who handed it
    /// over may end it, or whoever may manage others' delegations (<paramref name="canManageOthers"/>).</summary>
    Task<Result<EndDelegationResultDto>> EndAsync(Guid tenantId, Guid id, Guid callerUserId, bool canManageOthers, CancellationToken cancellationToken);
}

public sealed class ItemDelegationService(
    IItemDelegationRepository repository,
    IItemDelegationUnitOfWork unitOfWork,
    TimeProvider? timeProvider = null) : IItemDelegationService
{
    public async Task<Result<IReadOnlyList<ItemDelegationDto>>> ListMineAsync(Guid tenantId, Guid userId, bool activeOnly, CancellationToken cancellationToken) =>
        Result.Success<IReadOnlyList<ItemDelegationDto>>(
            (await repository.ListForUserAsync(tenantId, userId, activeOnly, cancellationToken)).Select(ToDto).ToList());

    public async Task<Result<ItemDelegationDto>> DelegateAsync(Guid tenantId, Guid callerUserId, DelegateItemRequest request, CancellationToken cancellationToken)
    {
        if (request.ItemId == Guid.Empty || request.DelegateUserId == Guid.Empty)
        {
            return Result.Failure<ItemDelegationDto>(Error.Validation("The item and the person to hand it to are required."));
        }

        if (!Enum.IsDefined(request.ItemType))
        {
            return Result.Failure<ItemDelegationDto>(Error.Validation("Unknown kind of item."));
        }

        if (request.DelegateUserId == callerUserId)
        {
            return Result.Failure<ItemDelegationDto>(Error.Validation("An item cannot be handed to yourself."));
        }

        var active = await repository.ListActiveForItemAsync(tenantId, request.ItemType, request.ItemId, cancellationToken);
        if (active.Any(d => d.DelegatorUserId == callerUserId))
        {
            return Result.Failure<ItemDelegationDto>(Error.Conflict("You have already handed this item over."));
        }

        // Handing it back to someone who handed it to you would make a loop; undo that hand-over instead.
        if (active.Any(d => d.DelegatorUserId == request.DelegateUserId))
        {
            return Result.Failure<ItemDelegationDto>(Error.Conflict("That person has already handed this item on; end that hand-over instead."));
        }

        var delegation = new ItemDelegation(Guid.NewGuid(), tenantId, request.ItemType, request.ItemId, request.ProjectId, callerUserId, request.DelegateUserId);
        await repository.AddAsync(delegation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(delegation));
    }

    public async Task<Result<EndDelegationResultDto>> EndAsync(Guid tenantId, Guid id, Guid callerUserId, bool canManageOthers, CancellationToken cancellationToken)
    {
        var delegation = await repository.GetByIdAsync(id, cancellationToken);
        if (delegation is null || delegation.TenantId != tenantId)
        {
            return Result.Failure<EndDelegationResultDto>(Error.NotFound("Delegation not found."));
        }

        if (!delegation.IsActive)
        {
            return Result.Failure<EndDelegationResultDto>(Error.Conflict("This hand-over has already ended."));
        }

        if (delegation.DelegatorUserId != callerUserId && !canManageOthers)
        {
            return Result.Failure<EndDelegationResultDto>(Error.Forbidden("Only the person who handed the item over can take it back."));
        }

        // Everything the delegate passed on after this one ends with it: follow the chain from them.
        var active = await repository.ListActiveForItemAsync(tenantId, delegation.ItemType, delegation.ItemId, cancellationToken);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var ended = new List<ItemDelegation> { delegation };
        var next = delegation.DelegateUserId;
        while (active.FirstOrDefault(d => d.DelegatorUserId == next && ended.All(e => e.Id != d.Id)) is { } following)
        {
            ended.Add(following);
            next = following.DelegateUserId;
        }

        foreach (var item in ended)
        {
            item.End(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new EndDelegationResultDto(delegation.DelegatorUserId, ended.Select(e => e.Id).ToList()));
    }

    private static ItemDelegationDto ToDto(ItemDelegation d) => new(
        d.Id, d.ItemType, d.ItemId, d.ProjectId, d.DelegatorUserId, d.DelegateUserId, d.IsActive, d.CreatedAtUtc, d.EndedAtUtc);
}
