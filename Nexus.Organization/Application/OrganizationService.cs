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
    IUserDirectory userDirectory,
    IOrganizationMemberRepository? members = null) : IOrganizationService
{
    private const string StructureOwnsPlace =
        "A unit's place in the chart and its code are changed in the organization structure, not here.";

    // A unit of another organization looks like a missing one, unless the caller may see every organization.
    private bool CanSee(OrganizationUnit unit) => currentUser.CanAccessTenant(unit.TenantId);

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
        if (unit is null || !CanSee(unit))
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

        // The organization comes from the caller's token. A body value for another organization
        // is honoured only for callers who may manage every organization.
        var tenantId = request.TenantId == Guid.Empty ? currentUser.TenantId : request.TenantId;
        if (tenantId is null || !currentUser.CanAccessTenant(tenantId))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Forbidden("You can only create units in your own organization."));
        }

        // A new unit is a flat one; where it goes in the chart is decided in the organization structure.
        if (request.ParentId is not null || !string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation(StructureOwnsPlace));
        }

        var unit = new OrganizationUnit(Guid.NewGuid(), tenantId.Value, request.Name);
        await repository.AddAsync(unit, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(unit, unit.Name));
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

        // A screen may send the whole unit back; only a different place or code is refused (null: as it is).
        if ((request.ParentId is not null && request.ParentId != unit.ParentUnitId) ||
            (!string.IsNullOrWhiteSpace(request.Code) && request.Code.Trim() != (unit.NodeCode ?? string.Empty)))
        {
            return Result.Failure<OrganizationUnitDto>(Error.Validation(StructureOwnsPlace));
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
        var paths = OrganizationPaths.For(await repository.ListAsync(unit.TenantId, cancellationToken));
        return Result.Success(ToDto(unit, paths.GetValueOrDefault(unit.Id).Path));
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken, bool includeBranch = false)
    {
        var unit = await repository.GetByIdAsync(id, cancellationToken);
        if (unit is null || !CanSee(unit))
        {
            return Result.Failure(Error.NotFound("Organization unit not found."));
        }

        // By default only the unit itself is deactivated, as it always was. With includeBranch its whole branch goes
        // with it (a sub-unit of an inactive unit makes no sense). Either way it is refused while anyone is still
        // placed in what is being deactivated: they would be left in a unit that no longer exists.
        var chart = await repository.ListAsync(unit.TenantId, cancellationToken);
        var branch = includeBranch ? OrganizationPaths.Descendants(chart, id) : new HashSet<Guid>();
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
            inBranch.Update(inBranch.Name, inBranch.ManagerUserId, isActive: false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static OrganizationUnitDto ToDto(OrganizationUnit unit, string? path) =>
        new(unit.Id, unit.TenantId, unit.Name, unit.ManagerUserId, unit.IsActive, unit.NodeCode ?? string.Empty, unit.ParentUnitId, path);
}
