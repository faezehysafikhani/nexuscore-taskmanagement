using Nexus.Organization.Application.Dtos;
using Nexus.Organization.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Organization.Application;

public interface IOrganizationMembershipService
{
    /// <summary>Everyone placed in the chart (optionally one unit), with the unit's name and full path.</summary>
    Task<Result<IReadOnlyList<OrganizationMemberDto>>> ListAsync(Guid tenantId, Guid? unitId, CancellationToken cancellationToken);

    /// <summary>Puts the user in the unit, or takes them out of the chart when <paramref name="unitId"/> is null.</summary>
    Task<Result<OrganizationMemberDto?>> SetUserUnitAsync(Guid tenantId, Guid userId, Guid? unitId, CancellationToken cancellationToken);
}

public sealed class OrganizationMembershipService(
    IOrganizationMemberRepository members,
    IOrganizationUnitRepository units,
    IOrganizationUnitOfWork unitOfWork) : IOrganizationMembershipService
{
    public async Task<Result<IReadOnlyList<OrganizationMemberDto>>> ListAsync(Guid tenantId, Guid? unitId, CancellationToken cancellationToken)
    {
        var chart = await units.ListAsync(tenantId, cancellationToken);
        var paths = OrganizationPaths.For(chart);
        var list = (await members.ListAsync(tenantId, unitId, cancellationToken))
            .Where(m => paths.ContainsKey(m.UnitId))
            .Select(m => new OrganizationMemberDto(m.UserId, m.UnitId, paths[m.UnitId].Name, paths[m.UnitId].Path))
            .ToList();
        return Result.Success<IReadOnlyList<OrganizationMemberDto>>(list);
    }

    public async Task<Result<OrganizationMemberDto?>> SetUserUnitAsync(Guid tenantId, Guid userId, Guid? unitId, CancellationToken cancellationToken)
    {
        var existing = await members.GetByUserAsync(tenantId, userId, cancellationToken);
        if (unitId is null)
        {
            if (existing is not null)
            {
                await members.RemoveAsync(existing, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success<OrganizationMemberDto?>(null);
        }

        var unit = await units.GetByIdAsync(unitId.Value, cancellationToken);
        if (unit is null || unit.TenantId != tenantId)
        {
            return Result.Failure<OrganizationMemberDto?>(Error.Validation("Organization unit was not found."));
        }

        if (!unit.IsActive)
        {
            return Result.Failure<OrganizationMemberDto?>(Error.Conflict("People cannot be placed in an inactive organization unit."));
        }

        if (existing is null)
        {
            await members.AddAsync(new OrganizationUnitMember(Guid.NewGuid(), tenantId, unit.Id, userId), cancellationToken);
        }
        else
        {
            existing.MoveTo(unit.Id);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var paths = OrganizationPaths.For(await units.ListAsync(tenantId, cancellationToken));
        return Result.Success<OrganizationMemberDto?>(new OrganizationMemberDto(userId, unit.Id, unit.Name, paths[unit.Id].Path));
    }
}
