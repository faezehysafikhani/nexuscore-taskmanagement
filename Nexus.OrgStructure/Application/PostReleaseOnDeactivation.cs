using NexusCore.Application.Identity.Interfaces;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// A disabled account leaves its posts: they fall vacant (where tasks are given to posts, their
/// tasks go to the substitution inbox), it is no longer a company's head, and its place keeps no
/// main position. Its company, unit and set manager stay, for when it is enabled again.
/// </summary>
public sealed class PostReleaseOnDeactivation(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    TaskPostDirectory taskPosts) : IUserDeactivationListener
{
    public async Task UserDeactivatedAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var freed = (await repository.ListUserPositionsAsync(tenantId, cancellationToken)).Where(h => h.UserId == userId).ToList();
        var assignment = await repository.GetAssignmentAsync(tenantId, userId, cancellationToken);
        var headOf = (await repository.ListCompaniesAsync(tenantId, cancellationToken)).Where(c => c.ManagerUserId == userId).ToList();
        if (freed.Count == 0 && assignment?.PositionId is null && headOf.Count == 0)
        {
            return;
        }

        freed.ForEach(repository.RemoveUserPosition);
        headOf.ForEach(company => company.SetHead(null));
        if (assignment is { PositionId: not null })
        {
            assignment.Set(assignment.CompanyId, assignment.UnitId, null, assignment.ManagerUserId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await taskPosts.SyncHoldersAsync(tenantId, freed.Select(h => h.UnitPositionId).ToList(), cancellationToken);
    }
}
