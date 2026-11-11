using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// The organization's plan (phase 9): how many users and how much file space it may use and until when, and what it
/// uses now. The limits are enforced where users and files are added - users through <see cref="IUserQuota"/>, files in
/// the file service - and the usage report is what lets people see how close they are.
/// </summary>
public sealed class MembershipService(
    IMembershipRepository repository,
    IUserDirectory userDirectory,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    TimeProvider clock) : IMembershipService, IUserQuota
{
    public const int ExpiringSoonDays = 14;
    public const double NearLimit = 0.9;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<Result<MembershipDto>> GetAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is not { } tenantId
            ? Result.Failure<MembershipDto>(Error.Unauthorized())
            : Result.Success(await BuildAsync(tenantId, cancellationToken));

    private async Task<int> ActiveUsersAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await userDirectory.CountBillableUsersAsync(tenantId, cancellationToken);

    private async Task<MembershipDto> BuildAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await repository.GetAsync(tenantId, cancellationToken);
        var usage = new MembershipUsageDto(
            await ActiveUsersAsync(tenantId, cancellationToken), await repository.StorageBytesAsync(tenantId, cancellationToken), await repository.TaskCountAsync(tenantId, cancellationToken));
        if (plan is null)
        {
            return new MembershipDto(null, null, null, null, null, null, MembershipStatus.NoPlan, null, usage, []);
        }

        var today = Today;
        var warnings = new List<string>();
        int? daysLeft = plan.ValidUntil is { } until ? until.DayNumber - today.DayNumber : null;
        var status = plan.NotStarted(today) ? MembershipStatus.NotStarted
            : plan.IsExpired(today) ? MembershipStatus.Expired
            : daysLeft is <= ExpiringSoonDays ? MembershipStatus.ExpiringSoon
            : MembershipStatus.Active;
        if (status == MembershipStatus.Expired) warnings.Add("The membership has expired: new users and files are refused.");
        if (status == MembershipStatus.ExpiringSoon) warnings.Add($"The membership ends in {daysLeft} day(s).");
        if (plan.MaxUsers is { } max && usage.ActiveUsers >= max * NearLimit) warnings.Add(usage.ActiveUsers >= max ? "The user limit is reached." : "The user limit is almost reached.");
        if (plan.MaxStorageMb is { } mb && usage.StorageBytes >= mb * 1024L * 1024L * NearLimit) warnings.Add(usage.StorageBytes >= mb * 1024L * 1024L ? "The storage limit is reached." : "The storage limit is almost reached.");
        return new MembershipDto(plan.PlanName, plan.MaxUsers, plan.MaxStorageMb, plan.ValidFrom, plan.ValidUntil, plan.Notes, status, daysLeft, usage, warnings);
    }

    public async Task<Result<MembershipDto>> SetAsync(SetMembershipRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<MembershipDto>(Error.Unauthorized());
        }

        if (string.IsNullOrWhiteSpace(request.PlanName) || request.PlanName.Length > 100
            || request.MaxUsers is < 1 or > 1_000_000 || request.MaxStorageMb is < 1 or > 100_000_000
            || (request.ValidFrom is { } from && request.ValidUntil is { } to && to < from))
        {
            return Result.Failure<MembershipDto>(Error.Validation("The plan needs a name, positive limits and an end date after its start."));
        }

        var plan = await repository.GetAsync(tenantId, cancellationToken);
        if (plan is null)
        {
            await repository.AddAsync(new TenantMembership(tenantId, request.PlanName, request.MaxUsers, request.MaxStorageMb, request.ValidFrom, request.ValidUntil, request.Notes), cancellationToken);
        }
        else
        {
            plan.Set(request.PlanName, request.MaxUsers, request.MaxStorageMb, request.ValidFrom, request.ValidUntil, request.Notes);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await BuildAsync(tenantId, cancellationToken));
    }

    public async Task<Result> ClearAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure(Error.Unauthorized());
        }

        if (await repository.GetAsync(tenantId, cancellationToken) is { } plan)
        {
            repository.Remove(plan);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Error?> CheckCanAddUserAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await repository.GetAsync(tenantId, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        if (plan.IsExpired(Today) || plan.NotStarted(Today))
        {
            return Error.Forbidden("اشتراک سازمان معتبر نیست؛ افزودن کاربر ممکن نیست. با پشتیبانی تماس بگیرید.");
        }

        return plan.MaxUsers is { } max && await ActiveUsersAsync(tenantId, cancellationToken) >= max
            ? Error.Forbidden($"سقف کاربران پلن ({max} نفر) پر شده است؛ برای افزودن کاربر پلن را ارتقا دهید.")
            : null;
    }

    public async Task<Error?> CheckStorageAsync(Guid tenantId, long addBytes, CancellationToken cancellationToken)
    {
        var plan = await repository.GetAsync(tenantId, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        if (plan.IsExpired(Today) || plan.NotStarted(Today))
        {
            return Error.Forbidden("اشتراک سازمان معتبر نیست؛ بارگذاری فایل ممکن نیست.");
        }

        return plan.MaxStorageMb is { } mb && await repository.StorageBytesAsync(tenantId, cancellationToken) + addBytes > mb * 1024L * 1024L
            ? Error.Forbidden($"سقف فضای پلن ({mb} مگابایت) پر شده است.")
            : null;
    }
}
