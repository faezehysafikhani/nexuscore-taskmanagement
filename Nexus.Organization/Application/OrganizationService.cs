using Nexus.Organization.Application.Dtos;
using Nexus.Organization.Domain;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Organization.Application;

public sealed class OrganizationService(
    IOrganizationUnitRepository repository,
    IOrganizationUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IUserDirectory userDirectory) : IOrganizationService
{
    // A unit of another organization looks like a missing one, unless the caller may see every organization.
    private bool CanSee(OrganizationUnit unit) => currentUser.CanAccessTenant(unit.TenantId);

    public async Task<Result<IReadOnlyList<OrganizationUnitDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var units = await repository.ListAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<OrganizationUnitDto>>(units.Select(ToDto).ToList());
    }

    public async Task<Result<OrganizationUnitDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var unit = await repository.GetByIdAsync(id, cancellationToken);
        return unit is null || !CanSee(unit)
            ? Result.Failure<OrganizationUnitDto>(Error.NotFound("Organization unit not found."))
            : Result.Success(ToDto(unit));
    }

    public async Task<Result<OrganizationUnitDto>> CreateAsync(CreateOrganizationUnitRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation("Name is required."));
        }

        // The organization comes from the caller's token. A body value for another organization
        // is honoured only for callers who may manage every organization.
        var tenantId = request.TenantId == Guid.Empty ? currentUser.TenantId : request.TenantId;
        if (tenantId is null || !currentUser.CanAccessTenant(tenantId))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Forbidden("You can only create units in your own organization."));
        }

        var unit = new OrganizationUnit(Guid.NewGuid(), tenantId.Value, request.Name);
        await repository.AddAsync(unit, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(unit));
    }

    public async Task<Result<OrganizationUnitDto>> UpdateAsync(Guid id, UpdateOrganizationUnitRequest request, CancellationToken cancellationToken)
    {
        var unit = await repository.GetByIdAsync(id, cancellationToken);
        if (unit is null || !CanSee(unit))
        {
            return Result.Failure<OrganizationUnitDto>(Error.NotFound("Organization unit not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation("Name is required."));
        }

        // A manager must be an active person of the unit's own organization: managers feed the
        // hierarchy that decides who sees whose tasks.
        if (request.ManagerUserId is { } managerId)
        {
            var manager = (await userDirectory.GetUsersAsync([managerId], cancellationToken)).FirstOrDefault();
            if (manager is null || manager.TenantId != unit.TenantId || !manager.IsActive)
            {
                return Result.Failure<OrganizationUnitDto>(Error.Validation("The manager must be an active user of this organization."));
            }
        }

        unit.Update(request.Name, request.ManagerUserId, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(unit));
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var unit = await repository.GetByIdAsync(id, cancellationToken);
        if (unit is null || !CanSee(unit))
        {
            return Result.Failure(Error.NotFound("Organization unit not found."));
        }

        unit.Update(unit.Name, unit.ManagerUserId, isActive: false);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static OrganizationUnitDto ToDto(OrganizationUnit unit) =>
        new(unit.Id, unit.TenantId, unit.Name, unit.ManagerUserId, unit.IsActive);
}
