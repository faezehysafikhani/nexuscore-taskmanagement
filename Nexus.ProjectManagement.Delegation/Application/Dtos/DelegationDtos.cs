using Nexus.ProjectManagement.Delegation.Domain;

namespace Nexus.ProjectManagement.Delegation.Application.Dtos;

public sealed record ItemDelegationDto(
    Guid Id, DelegatedItemType ItemType, Guid ItemId, Guid? ProjectId, Guid DelegatorUserId, Guid DelegateUserId,
    bool IsActive, DateTimeOffset DelegatedAtUtc, DateTimeOffset? EndedAtUtc);

/// <summary>The delegator is the caller.</summary>
public sealed record DelegateItemRequest(DelegatedItemType ItemType, Guid ItemId, Guid? ProjectId, Guid DelegateUserId);

/// <param name="RestoreToUserId">Who the item goes back to: the one who handed it over. The caller of the screen that
/// shows the item applies it (this module does not own the item).</param>
/// <param name="EndedIds">Every delegation ended: the one asked for and everything handed on after it.</param>
public sealed record EndDelegationResultDto(Guid RestoreToUserId, IReadOnlyList<Guid> EndedIds);
