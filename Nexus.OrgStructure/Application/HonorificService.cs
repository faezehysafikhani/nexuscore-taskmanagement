using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Titles put before people's names («آقای»، «خانم»، «جناب مهندس»...): the organization's list of
/// them, and which one each user has. A user's display name starts with their title, so every
/// part of the system shows it (IUserTitleWriter keeps the name in step).
/// </summary>
public interface IHonorificService
{
    Task<Result<IReadOnlyList<HonorificDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<HonorificDto>> CreateAsync(Guid tenantId, CreateHonorificRequest request, CancellationToken cancellationToken);
    Task<Result<HonorificDto>> UpdateAsync(Guid tenantId, Guid id, UpdateHonorificRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<Result<UserTitleDto>> GetUserTitleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<Result<UserTitleDto>> SetUserTitleAsync(Guid tenantId, Guid userId, SetUserTitleRequest request, CancellationToken cancellationToken);
}

public sealed class HonorificService(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    IUserDirectory users,
    IUserTitleWriter titleWriter) : IHonorificService
{
    private const int MaxTitleLength = 40;

    public async Task<Result<IReadOnlyList<HonorificDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var honorifics = await repository.ListHonorificsAsync(tenantId, cancellationToken);
        var holders = await repository.ListUserHonorificsAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<HonorificDto>>(honorifics.Select(h => ToDto(h, holders)).ToList());
    }

    public async Task<Result<HonorificDto>> CreateAsync(Guid tenantId, CreateHonorificRequest request, CancellationToken cancellationToken)
    {
        var invalid = await ValidateAsync(tenantId, null, request.Title, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<HonorificDto>(invalid);
        }

        var existing = await repository.ListHonorificsAsync(tenantId, cancellationToken);
        var sortOrder = request.SortOrder ?? (existing.Count == 0 ? 1 : existing.Max(h => h.SortOrder) + 1);
        var honorific = new Honorific(Guid.NewGuid(), tenantId, request.Title, sortOrder);
        await repository.AddHonorificAsync(honorific, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(honorific, []));
    }

    public async Task<Result<HonorificDto>> UpdateAsync(Guid tenantId, Guid id, UpdateHonorificRequest request, CancellationToken cancellationToken)
    {
        var honorific = await repository.GetHonorificAsync(tenantId, id, cancellationToken);
        if (honorific is null)
        {
            return Result.Failure<HonorificDto>(Error.NotFound("عنوان پیدا نشد."));
        }

        var invalid = await ValidateAsync(tenantId, id, request.Title, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<HonorificDto>(invalid);
        }

        var previousTitle = honorific.Title;
        honorific.Update(request.Title, request.SortOrder, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // A renamed title is renamed in the names of everyone who has it.
        var holders = (await repository.ListUserHonorificsAsync(tenantId, cancellationToken)).Where(h => h.HonorificId == id).ToList();
        if (previousTitle != honorific.Title)
        {
            foreach (var holder in holders)
            {
                await titleWriter.ApplyTitleAsync(tenantId, holder.UserId, previousTitle, honorific.Title, cancellationToken);
            }
        }

        return Result.Success(ToDto(honorific, holders));
    }

    public async Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var honorific = await repository.GetHonorificAsync(tenantId, id, cancellationToken);
        if (honorific is null)
        {
            return Result.Failure(Error.NotFound("عنوان پیدا نشد."));
        }

        if ((await repository.ListUserHonorificsAsync(tenantId, cancellationToken)).Any(h => h.HonorificId == id))
        {
            return Result.Failure(Error.Conflict("چند کاربر این عنوان را دارند؛ اول عنوان آن‌ها را عوض کنید، یا به‌جای حذف، آن را غیرفعال کنید."));
        }

        repository.RemoveHonorific(honorific);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<UserTitleDto>> GetUserTitleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var current = await repository.GetUserHonorificAsync(tenantId, userId, cancellationToken);
        var honorific = current is null ? null : await repository.GetHonorificAsync(tenantId, current.HonorificId, cancellationToken);
        return Result.Success(new UserTitleDto(userId, honorific?.Id, honorific?.Title));
    }

    public async Task<Result<UserTitleDto>> SetUserTitleAsync(Guid tenantId, Guid userId, SetUserTitleRequest request, CancellationToken cancellationToken)
    {
        if (!(await users.GetUsersAsync([userId], cancellationToken)).Any(u => u.Id == userId && u.TenantId == tenantId))
        {
            return Result.Failure<UserTitleDto>(Error.NotFound("کاربر پیدا نشد."));
        }

        Honorific? title = null;
        if (request.HonorificId is { } honorificId)
        {
            title = await repository.GetHonorificAsync(tenantId, honorificId, cancellationToken);
            if (title is null)
            {
                return Result.Failure<UserTitleDto>(Error.Validation("عنوان پیدا نشد."));
            }
        }

        var current = await repository.GetUserHonorificAsync(tenantId, userId, cancellationToken);
        var previous = current is null ? null : await repository.GetHonorificAsync(tenantId, current.HonorificId, cancellationToken);

        // An inactive title is kept by whoever has it, but cannot be newly given.
        if (title is { IsActive: false } && title.Id != previous?.Id)
        {
            return Result.Failure<UserTitleDto>(Error.Validation("این عنوان غیرفعال است."));
        }

        // The name first: if it cannot change (the system administrator), nothing is saved.
        var applied = await titleWriter.ApplyTitleAsync(tenantId, userId, previous?.Title, title?.Title, cancellationToken);
        if (applied.IsFailure)
        {
            return Result.Failure<UserTitleDto>(applied.Error);
        }

        if (title is null)
        {
            if (current is not null)
            {
                repository.RemoveUserHonorific(current);
            }
        }
        else if (current is null)
        {
            await repository.AddUserHonorificAsync(new UserHonorific(Guid.NewGuid(), tenantId, userId, title.Id), cancellationToken);
        }
        else
        {
            current.Change(title.Id);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new UserTitleDto(userId, title?.Id, title?.Title));
    }

    private async Task<Error?> ValidateAsync(Guid tenantId, Guid? id, string? title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("عنوان را وارد کنید.");
        }

        if (title.Trim().Length > MaxTitleLength)
        {
            return Error.Validation("عنوان بیش از حد بلند است.");
        }

        return await repository.HonorificTitleExistsAsync(tenantId, title.Trim(), id, cancellationToken)
            ? Error.Conflict("این عنوان از قبل هست.")
            : null;
    }

    private static HonorificDto ToDto(Honorific honorific, IReadOnlyList<UserHonorific> holders) =>
        new(honorific.Id, honorific.Title, honorific.SortOrder, honorific.IsActive, holders.Count(h => h.HonorificId == honorific.Id));
}
