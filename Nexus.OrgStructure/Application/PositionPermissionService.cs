using Nexus.OrgStructure.Domain;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>Permission names a position, or one post, gives to whoever holds it.</summary>
public sealed record PositionPermissionsDto(Guid Id, IReadOnlyList<string> PermissionNames);

public sealed record SetPositionPermissionsRequest(IReadOnlyList<string> PermissionNames);

/// <summary>
/// Which permissions each position (سِمَت) gives to its holders IN EACH COMPANY, and each post to its holder. What a user ends up with
/// is worked out by the Core (IPositionPermissionProvider); this only keeps the lists.
/// </summary>
public interface IPositionPermissionService
{
    Task<Result<PositionPermissionsDto>> GetPositionAsync(Guid tenantId, Guid companyId, Guid positionId, CancellationToken cancellationToken);
    Task<Result<PositionPermissionsDto>> SetPositionAsync(Guid tenantId, Guid companyId, Guid positionId, SetPositionPermissionsRequest request, CancellationToken cancellationToken);
    Task<Result<PositionPermissionsDto>> GetPostAsync(Guid tenantId, Guid unitPositionId, CancellationToken cancellationToken);
    Task<Result<PositionPermissionsDto>> SetPostAsync(Guid tenantId, Guid unitPositionId, SetPositionPermissionsRequest request, CancellationToken cancellationToken);
}

/// <summary>What the permission lists of positions and posts need from the store (kept apart from the structure's own repository).</summary>
public interface IPositionPermissionRepository
{
    Task<IReadOnlyList<string>> ListPositionPermissionNamesAsync(Guid tenantId, Guid companyId, Guid positionId, CancellationToken cancellationToken);
    Task SetPositionPermissionsAsync(Guid tenantId, Guid companyId, Guid positionId, IReadOnlyCollection<string> names, CancellationToken cancellationToken);
    Task<UnitPosition?> GetUnitPositionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListUnitPositionPermissionNamesAsync(Guid tenantId, Guid unitPositionId, CancellationToken cancellationToken);
    Task SetUnitPositionPermissionsAsync(Guid tenantId, Guid unitPositionId, IReadOnlyCollection<string> names, CancellationToken cancellationToken);
}

public sealed class PositionPermissionService(
    IOrgStructureRepository structure,
    IPositionPermissionRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    IEnumerable<IPermissionCatalog> catalogs,
    ICurrentUserContext currentUser,
    CompanyReachScope? companyReach = null) : IPositionPermissionService
{
    /// <summary>The position is one of this company's, and the company is one the caller reaches (not kept to another).</summary>
    private async Task<Error?> CheckPositionInCompanyAsync(Guid tenantId, Guid companyId, Guid positionId, CancellationToken cancellationToken)
    {
        if (await structure.GetPositionAsync(tenantId, positionId, cancellationToken) is null
            || await structure.GetCompanyAsync(tenantId, companyId, cancellationToken) is null
            || !(await structure.ListPositionCompaniesAsync(tenantId, cancellationToken)).Any(x => x.PositionId == positionId && x.CompanyId == companyId))
        {
            return Error.NotFound("سمت در این شرکت پیدا نشد.");
        }

        return await IsMineAsync(tenantId, companyId, cancellationToken) ? null : Error.Forbidden("فقط شرکت خودتان را می‌توانید ببینید و تغییر دهید.");
    }

    private async Task<bool> IsMineAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
        companyReach is null || currentUser.UserId is not { } me
        || await companyReach.GetCompaniesAsync(tenantId, me, cancellationToken) is not { } mine || mine.Contains(companyId);

    public async Task<Result<PositionPermissionsDto>> GetPositionAsync(Guid tenantId, Guid companyId, Guid positionId, CancellationToken cancellationToken) =>
        await CheckPositionInCompanyAsync(tenantId, companyId, positionId, cancellationToken) is { } problem
            ? Result.Failure<PositionPermissionsDto>(problem)
            : Result.Success(new PositionPermissionsDto(positionId, await repository.ListPositionPermissionNamesAsync(tenantId, companyId, positionId, cancellationToken)));

    public async Task<Result<PositionPermissionsDto>> SetPositionAsync(Guid tenantId, Guid companyId, Guid positionId, SetPositionPermissionsRequest request, CancellationToken cancellationToken)
    {
        if (await CheckPositionInCompanyAsync(tenantId, companyId, positionId, cancellationToken) is { } problem)
        {
            return Result.Failure<PositionPermissionsDto>(problem);
        }

        var current = await repository.ListPositionPermissionNamesAsync(tenantId, companyId, positionId, cancellationToken);
        var names = Check(request.PermissionNames, current, out var failure);
        if (failure is not null)
        {
            return Result.Failure<PositionPermissionsDto>(failure);
        }

        await repository.SetPositionPermissionsAsync(tenantId, companyId, positionId, names, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new PositionPermissionsDto(positionId, names));
    }

    public async Task<Result<PositionPermissionsDto>> GetPostAsync(Guid tenantId, Guid unitPositionId, CancellationToken cancellationToken) =>
        await repository.GetUnitPositionAsync(tenantId, unitPositionId, cancellationToken) is not { } seat || !await IsMineAsync(tenantId, seat.CompanyId, cancellationToken)
            ? Result.Failure<PositionPermissionsDto>(Error.NotFound("پست پیدا نشد."))
            : Result.Success(new PositionPermissionsDto(unitPositionId, await repository.ListUnitPositionPermissionNamesAsync(tenantId, unitPositionId, cancellationToken)));

    public async Task<Result<PositionPermissionsDto>> SetPostAsync(Guid tenantId, Guid unitPositionId, SetPositionPermissionsRequest request, CancellationToken cancellationToken)
    {
        if (await repository.GetUnitPositionAsync(tenantId, unitPositionId, cancellationToken) is not { } seat || !await IsMineAsync(tenantId, seat.CompanyId, cancellationToken))
        {
            return Result.Failure<PositionPermissionsDto>(Error.NotFound("پست پیدا نشد."));
        }

        var current = await repository.ListUnitPositionPermissionNamesAsync(tenantId, unitPositionId, cancellationToken);
        var names = Check(request.PermissionNames, current, out var failure);
        if (failure is not null)
        {
            return Result.Failure<PositionPermissionsDto>(failure);
        }

        await repository.SetUnitPositionPermissionsAsync(tenantId, unitPositionId, names, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new PositionPermissionsDto(unitPositionId, names));
    }

    /// <summary>
    /// Only known permissions; and, like on a role or a user, a caller can only hand out what they hold
    /// themselves (a permission already on the list may stay without it), so this cannot be used to escalate.
    /// </summary>
    private List<string> Check(IReadOnlyList<string>? requested, IReadOnlyList<string> current, out Error? failure)
    {
        failure = null;
        var names = (requested ?? []).Select(name => name?.Trim() ?? string.Empty).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var known = catalogs.SelectMany(catalog => catalog.GetPermissions()).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = names.Where(name => !known.Contains(name)).ToList();
        if (unknown.Count > 0)
        {
            failure = Error.Validation("دسترسی ناشناخته: " + string.Join(", ", unknown));
            return names;
        }

        var changed = names.Except(current, StringComparer.Ordinal).Concat(current.Except(names, StringComparer.Ordinal)).ToList();
        var notHeld = changed.Where(name => !currentUser.HasPermission(name)).ToList();
        if (notHeld.Count > 0)
        {
            failure = Error.Forbidden("You can only grant permissions you have yourself: " + string.Join(", ", notHeld));
        }

        return names;
    }
}
