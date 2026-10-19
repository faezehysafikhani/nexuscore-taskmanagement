using Nexus.ProjectManagement.Raci.Application.Dtos;
using Nexus.ProjectManagement.Raci.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Raci.Application;

public sealed class RaciService(IRaciRepository repository, IRaciUnitOfWork unitOfWork) : IRaciService
{
    public const int MaxEntriesPerItem = 100;

    public async Task<Result<IReadOnlyList<RaciAssignmentDto>>> ListByProjectAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var all = await repository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<RaciAssignmentDto>>(all.Where(a => a.TenantId == tenantId).Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<RaciAssignmentDto>>> SetItemAsync(Guid tenantId, SetItemRaciRequest request, CancellationToken cancellationToken)
    {
        if (request.ProjectId == Guid.Empty || request.ItemId == Guid.Empty)
        {
            return Result.Failure<IReadOnlyList<RaciAssignmentDto>>(Error.Validation("The project and the item are required."));
        }

        if (!Enum.IsDefined(request.ItemType) || request.Entries.Any(e => !Enum.IsDefined(e.Role)))
        {
            return Result.Failure<IReadOnlyList<RaciAssignmentDto>>(Error.Validation("Unknown kind of item or role."));
        }

        if (request.Entries.Any(e => e.UserId == Guid.Empty))
        {
            return Result.Failure<IReadOnlyList<RaciAssignmentDto>>(Error.Validation("Every role needs a person."));
        }

        var wanted = request.Entries.Distinct().ToList();
        if (wanted.Count > MaxEntriesPerItem)
        {
            return Result.Failure<IReadOnlyList<RaciAssignmentDto>>(Error.Validation($"An item has at most {MaxEntriesPerItem} roles."));
        }

        // The "A" is one person: the one who answers for the item.
        if (wanted.Count(e => e.Role == RaciRole.Accountable) > 1)
        {
            return Result.Failure<IReadOnlyList<RaciAssignmentDto>>(Error.Validation("An item has only one accountable person."));
        }

        var current = (await repository.ListByItemAsync(request.ProjectId, request.ItemType, request.ItemId, cancellationToken))
            .Where(a => a.TenantId == tenantId).ToList();

        foreach (var stale in current.Where(a => !wanted.Contains(new RaciEntryInput(a.UserId, a.Role))))
        {
            await repository.RemoveAsync(stale, cancellationToken);
        }

        foreach (var entry in wanted.Where(e => current.All(a => !(a.UserId == e.UserId && a.Role == e.Role))))
        {
            await repository.AddAsync(new RaciAssignment(Guid.NewGuid(), tenantId, request.ProjectId, request.ItemType, request.ItemId, entry.UserId, entry.Role), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ListByProjectAsync(tenantId, request.ProjectId, cancellationToken);
    }

    private static RaciAssignmentDto ToDto(RaciAssignment a) => new(a.Id, a.ProjectId, a.ItemType, a.ItemId, a.UserId, a.Role);
}
