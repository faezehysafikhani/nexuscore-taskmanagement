using Nexus.Organization.Application.Dtos;
using Nexus.Organization.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Organization.Application;

public sealed class OrganizationService(
    IOrganizationUnitRepository repository,
    IOrganizationUnitOfWork unitOfWork,
    IOrganizationMemberRepository? members = null) : IOrganizationService
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
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation("Name is required."));
        }

        var code = request.Code;
        if (string.IsNullOrWhiteSpace(code))
        {
            code = await NextCodeAsync(request.TenantId, cancellationToken);
        }
        else if (await repository.CodeExistsAsync(request.TenantId, code, null, cancellationToken))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Conflict("An organization unit with this code already exists."));
        }

        if (request.ParentId is { } parentId && await repository.GetByIdAsync(parentId, cancellationToken) is null)
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation("Parent organization unit was not found."));
        }

        var unit = new OrganizationUnit(Guid.NewGuid(), request.TenantId, request.Name, code, request.ParentId);
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

        // Deactivating a unit takes its whole branch with it (a sub-unit of an inactive unit makes no sense), and is
        // refused while anyone is still placed in the branch: they would be left in a unit that no longer exists.
        var chart = await repository.ListAsync(unit.TenantId, cancellationToken);
        var branch = OrganizationPaths.Descendants(chart, id);
        branch.Add(id);
        if (members is not null)
        {
            var placed = (await members.ListAsync(unit.TenantId, null, cancellationToken)).Count(m => branch.Contains(m.UnitId));
            if (placed > 0)
            {
                return Result.Failure(Error.Conflict($"{placed} people are placed in this unit or its sub-units; move them first."));
            }
        }

        foreach (var inBranch in chart.Where(u => branch.Contains(u.Id)))
        {
            inBranch.Update(inBranch.Name, inBranch.Code, inBranch.ParentId, inBranch.ManagerUserId, isActive: false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<string> NextCodeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var next = (await repository.ListAsync(tenantId, cancellationToken)).Count + 1;
        while (await repository.CodeExistsAsync(tenantId, $"U{next:D4}", null, cancellationToken))
        {
            next++;
        }

        return $"U{next:D4}";
    }

    private static OrganizationUnitDto ToDto(OrganizationUnit unit, string? path) =>
        new(unit.Id, unit.TenantId, unit.Name, unit.Code, unit.ParentId, unit.ManagerUserId, unit.IsActive, path);
}
