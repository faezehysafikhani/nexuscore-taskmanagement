using Nexus.ProjectManagement.Raci.Domain;

namespace Nexus.ProjectManagement.Raci.Application.Dtos;

public sealed record RaciAssignmentDto(Guid Id, Guid ProjectId, RaciItemType ItemType, Guid ItemId, Guid UserId, RaciRole Role);

public sealed record RaciEntryInput(Guid UserId, RaciRole Role);

/// <summary>Makes the roles of one item exactly <paramref name="Entries"/> (an empty list clears the item).</summary>
public sealed record SetItemRaciRequest(Guid ProjectId, RaciItemType ItemType, Guid ItemId, IReadOnlyList<RaciEntryInput> Entries);
