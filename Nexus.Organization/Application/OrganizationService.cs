using Nexus.Organization.Application.Dtos;
using Nexus.Organization.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Organization.Application;

public sealed class OrganizationService(
    IOrganizationUnitRepository repository,
    IOrganizationUnitOfWork unitOfWork) : IOrganizationService
{
    public async Task<Result<IReadOnlyList<OrganizationUnitDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken, bool activeOnly = false)
    {
        var units = await repository.ListAsync(tenantId, cancellationToken);
        var paths = OrganizationPaths.For(units);
        return Result.Success<IReadOnlyList<OrganizationUnitDto>>(
            units.Where(u => !activeOnly || u.IsActive).Select(u => ToDto(u, paths[u.Id].Path)).ToList());
    }

    public async Task<Result<OrganizationUnitDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var unit = await repository.GetByIdAsync(id, cancellationToken);
        if (unit is null)
        {
            return Result.Failure<OrganizationUnitDto>(Error.NotFound("Organization unit not found."));
        }

        var paths = OrganizationPaths.For(await repository.ListAsync(unit.TenantId, cancellationToken));
        return Result.Success(ToDto(unit, paths.GetValueOrDefault(unit.Id).Path));
    }

    public async Task<Result<OrganizationUnitDto>> CreateAsync(CreateOrganizationUnitRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation("Name and code are required."));
        }

        if (await repository.CodeExistsAsync(request.TenantId, request.Code, null, cancellationToken))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Conflict("An organization unit with this code already exists."));
        }

        if (request.ParentId is { } parentId && await repository.GetByIdAsync(parentId, cancellationToken) is null)
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation("Parent organization unit was not found."));
        }

        var unit = new OrganizationUnit(Guid.NewGuid(), request.TenantId, request.Name, request.Code, request.ParentId);
        await repository.AddAsync(unit, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var createdPaths = OrganizationPaths.For(await repository.ListAsync(unit.TenantId, cancellationToken));
        return Result.Success(ToDto(unit, createdPaths[unit.Id].Path));
    }

    public async Task<Result<OrganizationUnitDto>> UpdateAsync(Guid id, UpdateOrganizationUnitRequest request, CancellationToken cancellationToken)
    {
        var unit = await repository.GetByIdAsync(id, cancellationToken);
        if (unit is null)
        {
            return Result.Failure<OrganizationUnitDto>(Error.NotFound("Organization unit not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation("Name and code are required."));
        }

        if (await repository.CodeExistsAsync(unit.TenantId, request.Code, id, cancellationToken))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Conflict("An organization unit with this code already exists."));
        }

        if (request.ParentId == id)
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation("An organization unit cannot be its own parent."));
        }

        var chart = await repository.ListAsync(unit.TenantId, cancellationToken);
        if (request.ParentId is { } newParent && chart.All(u => u.Id != newParent))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation("Parent organization unit was not found."));
        }

        // A unit cannot be moved under itself or one of its own descendants: the chart would loop.
        if (OrganizationPaths.WouldLoop(chart, id, request.ParentId))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation("An organization unit cannot be moved under one of its own sub-units."));
        }

        unit.Update(request.Name, request.Code, request.ParentId, request.ManagerUserId, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var updatedPaths = OrganizationPaths.For(chart);
        return Result.Success(ToDto(unit, updatedPaths[unit.Id].Path));
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var unit = await repository.GetByIdAsync(id, cancellationToken);
        if (unit is null)
        {
            return Result.Failure(Error.NotFound("Organization unit not found."));
        }

        unit.Update(unit.Name, unit.Code, unit.ParentId, unit.ManagerUserId, isActive: false);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static OrganizationUnitDto ToDto(OrganizationUnit unit, string? path) =>
        new(unit.Id, unit.TenantId, unit.Name, unit.Code, unit.ParentId, unit.ManagerUserId, unit.IsActive, path);
}
