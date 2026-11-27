using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Phase 10: what happens to a person's posts when they leave, and when a role moves to someone else.
/// Both are the appointment changes already used one post at a time, made in one save inside the
/// organization's turn, so a half-done change is never visible; tasks given to the posts follow their
/// holders, and each person's place stays in step with what they hold.
/// </summary>
public sealed partial class OrgStructureService
{
    public async Task<Result<OffboardResultDto>> OffboardUserAsync(Guid tenantId, Guid userId, OffboardUserRequest request, CancellationToken cancellationToken)
    {
        if (request.SuccessorUserId == userId)
        {
            return Result.Failure<OffboardResultDto>(Error.Validation("جانشین نمی‌تواند خودِ همین فرد باشد."));
        }

        if (request.SuccessorUserId is null && request.PostIds is { Count: > 0 })
        {
            return Result.Failure<OffboardResultDto>(Error.Validation("برای دادن پست‌ها به کسی، جانشین را انتخاب کنید."));
        }

        var (turn, busy) = await AppointmentTurnAsync(tenantId, cancellationToken);
        if (busy is not null)
        {
            return Result.Failure<OffboardResultDto>(busy);
        }

        await using var _ = turn;
        var now = Now;
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var person = (await users.GetUsersAsync([userId], cancellationToken)).FirstOrDefault();
        if (person is null || person.TenantId != tenantId)
        {
            return Result.Failure<OffboardResultDto>(Error.NotFound("کارمند پیدا نشد."));
        }

        var seats = snapshot.Seats.ToDictionary(seat => seat.Id);
        var all = await repository.ListUserPositionHistoryAsync(tenantId, null, null, cancellationToken);
        var byPost = all.ToLookup(a => a.UnitPositionId);
        var history = all.Where(a => a.UserId == userId).ToList();
        var current = history.Where(a => a.IsActiveAt(now)).ToList();
        var upcoming = history.Where(a => a.StartDate > now).ToList();
        var headOf = snapshot.Companies.Values.Where(company => company.ManagerUserId == userId).ToList();
        if (current.Count == 0 && upcoming.Count == 0 && headOf.Count == 0)
        {
            return Result.Failure<OffboardResultDto>(Error.Validation("این کارمند انتصاب فعالی ندارد."));
        }

        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        if (mine is not null && history.Any(a => seats.GetValueOrDefault(a.UnitPositionId) is { } seat && !mine.Contains(seat.CompanyId)))
        {
            return Result.Failure<OffboardResultDto>(OtherCompany());
        }

        // Posts that go straight to a successor: at once, so the posts are never without someone to hand them to.
        var taking = new List<UserPosition>();
        if (request.SuccessorUserId is { } successorId)
        {
            var successor = (await users.GetUsersAsync([successorId], cancellationToken)).FirstOrDefault();
            if (successor is null || successor.TenantId != tenantId || !successor.IsActive)
            {
                return Result.Failure<OffboardResultDto>(Error.Validation("کارمند جانشین باید فعال و از همین مجموعه باشد."));
            }

            var wanted = request.PostIds?.ToHashSet();
            taking = current.Where(a => wanted is null || wanted.Contains(a.UnitPositionId)).ToList();
            if (wanted is not null && taking.Count != wanted.Count)
            {
                return Result.Failure<OffboardResultDto>(Error.Validation("بعضی از پست‌های انتخاب‌شده در اختیار این کارمند نیستند."));
            }

            var place = snapshot.Assignments.FirstOrDefault(a => a.UserId == successorId);
            if (taking.Any(a => seats.GetValueOrDefault(a.UnitPositionId) is { } seat && (place is null || place.CompanyId != seat.CompanyId)))
            {
                return Result.Failure<OffboardResultDto>(Error.Validation(place is null
                    ? "جانشین هنوز در سازمان جایی ندارد؛ اول واحد و سمت او را ثبت کنید."
                    : "جانشین باید در همان شرکتِ هر پست، جای سازمانی داشته باشد."));
            }

            if (taking.Any(a => upcoming.Any(u => u.UnitPositionId == a.UnitPositionId)))
            {
                return Result.Failure<OffboardResultDto>(Error.Conflict("یکی از پست‌های انتخاب‌شده انتصاب آینده دارد؛ اول آن را لغو کنید."));
            }
        }

        // Appointments that had not started: they never will, so they go (as a cancelled one, not ended);
        // whoever was to end when it started stays on.
        foreach (var notStarted in upcoming)
        {
            foreach (var open in byPost[notStarted.UnitPositionId].Where(a => a.IsActiveAt(now) && a.EndDate == notStarted.StartDate))
            {
                open.Reopen();
            }

            repository.RemoveUserPosition(notStarted);
        }

        var transferred = new List<UserPosition>();
        foreach (var appointment in current)
        {
            appointment.End(now);
            if (request.SuccessorUserId is { } nextUserId && taking.Any(t => t.Id == appointment.Id))
            {
                var added = new UserPosition(Guid.NewGuid(), tenantId, nextUserId, appointment.UnitPositionId, now);
                await repository.AddUserPositionAsync(added, cancellationToken);
                transferred.Add(added);
            }
        }

        // A company head who leaves takes the head's post with them: it goes to the successor who took
        // that post, or the company is left with no one as its head.
        var headless = new List<Guid>();
        foreach (var company in headOf)
        {
            var keptBySuccessor = request.SuccessorUserId is { } nextUserId
                && transferred.Any(a => seats.GetValueOrDefault(a.UnitPositionId) is { } seat
                    && seat.CompanyId == company.Id && snapshot.IsHeadPost(seat));
            company.SetHead(keptBySuccessor ? request.SuccessorUserId : null);
            if (!keptBySuccessor)
            {
                headless.Add(company.Id);
            }
        }

        if (!await SaveAppointmentsAsync(turn, cancellationToken))
        {
            return Result.Failure<OffboardResultDto>(ChangedMeanwhile());
        }

        var posts = current.Select(a => a.UnitPositionId).Concat(upcoming.Select(a => a.UnitPositionId)).Distinct().ToList();
        if (taskPosts is not null && posts.Count > 0)
        {
            await taskPosts.SyncHoldersAsync(tenantId, posts, cancellationToken);
        }

        await RehomeAsync(tenantId, request.SuccessorUserId is { } handOver ? [userId, handOver] : [userId], cancellationToken);

        return Result.Success(new OffboardResultDto(
            userId,
            await AppointmentDtosAsync(tenantId, snapshot, current.Where(a => transferred.All(t => t.UnitPositionId != a.UnitPositionId)).ToList(), cancellationToken),
            await AppointmentDtosAsync(tenantId, snapshot, transferred, cancellationToken),
            await AppointmentDtosAsync(tenantId, snapshot, upcoming, cancellationToken),
            headless));
    }

    public async Task<Result<TransferPostsResultDto>> TransferPostsAsync(Guid tenantId, Guid userId, TransferPostsRequest request, CancellationToken cancellationToken)
    {
        if (request.ToUserId == userId)
        {
            return Result.Failure<TransferPostsResultDto>(Error.Validation("انتقال پست‌ها به خودِ همین فرد معنا ندارد."));
        }

        var (turn, busy) = await AppointmentTurnAsync(tenantId, cancellationToken);
        if (busy is not null)
        {
            return Result.Failure<TransferPostsResultDto>(busy);
        }

        await using var _ = turn;
        var now = Now;
        var start = request.EffectiveDate ?? now;
        if (start < now - Slack)
        {
            return Result.Failure<TransferPostsResultDto>(Error.Validation("انتقال از روزی که گذشته ثبت نمی‌شود؛ سابقه بازنویسی نمی‌شود."));
        }

        start = start < now ? now : start;
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var from = (await users.GetUsersAsync([userId], cancellationToken)).FirstOrDefault();
        if (from is null || from.TenantId != tenantId)
        {
            return Result.Failure<TransferPostsResultDto>(Error.NotFound("کارمند پیدا نشد."));
        }

        var to = (await users.GetUsersAsync([request.ToUserId], cancellationToken)).FirstOrDefault();
        if (to is null || to.TenantId != tenantId || !to.IsActive)
        {
            return Result.Failure<TransferPostsResultDto>(Error.Validation("کارمند گیرنده باید فعال و از همین مجموعه باشد."));
        }

        var history = await repository.ListUserPositionHistoryAsync(tenantId, null, null, cancellationToken);
        var byPost = history.ToLookup(a => a.UnitPositionId);
        var seats = snapshot.Seats.ToDictionary(seat => seat.Id);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        var place = snapshot.Assignments.FirstOrDefault(a => a.UserId == request.ToUserId);

        var candidates = history.Where(a => a.UserId == userId && a.IsActiveAt(now)).ToList();
        if (request.PostIds is { } ids)
        {
            var set = ids.ToHashSet();
            candidates = candidates.Where(a => set.Contains(a.UnitPositionId)).ToList();
            if (candidates.Count != set.Count)
            {
                return Result.Failure<TransferPostsResultDto>(Error.Validation("بعضی از این پست‌ها در اختیار این فرد نیستند."));
            }
        }

        if (candidates.Count == 0)
        {
            return Result.Failure<TransferPostsResultDto>(Error.Validation("این فرد پست فعالی برای انتقال ندارد."));
        }

        var moving = new List<UserPosition>();
        foreach (var appointment in candidates)
        {
            if (!seats.TryGetValue(appointment.UnitPositionId, out var seat) || !snapshot.IsShown(seat))
            {
                return Result.Failure<TransferPostsResultDto>(Error.NotFound("یکی از پست‌های این فرد پیدا نشد."));
            }

            if (mine is not null && !mine.Contains(seat.CompanyId))
            {
                return Result.Failure<TransferPostsResultDto>(OtherCompany());
            }

            if (snapshot.Positions.GetValueOrDefault(seat.PositionId) is not { IsActive: true })
            {
                return Result.Failure<TransferPostsResultDto>(Error.Validation("سمت یکی از این پست‌ها غیرفعال است."));
            }

            if (place is null || place.CompanyId != seat.CompanyId)
            {
                return Result.Failure<TransferPostsResultDto>(Error.Validation(place is null
                    ? "گیرنده هنوز در سازمان جایی ندارد؛ اول واحد و سمت او را ثبت کنید."
                    : "گیرنده باید در همان شرکتِ هر پست، جای سازمانی داشته باشد."));
            }

            var rows = byPost[appointment.UnitPositionId];
            if (rows.Any(a => a.UserId == request.ToUserId && a.IsActiveAt(start)))
            {
                return Result.Failure<TransferPostsResultDto>(Error.Conflict("گیرنده همین حالا این پست را دارد."));
            }

            if (rows.Any(a => a.StartDate > now))
            {
                return Result.Failure<TransferPostsResultDto>(Error.Conflict("یکی از این پست‌ها انتصاب آینده دارد؛ اول آن را لغو کنید."));
            }

            moving.Add(appointment);
        }

        var added = moving
            .Select(a => new UserPosition(Guid.NewGuid(), tenantId, request.ToUserId, a.UnitPositionId, start))
            .ToList();

        // Taking these now must not make a management loop (a later start is checked again when it starts).
        if (start <= now)
        {
            var after = snapshot.Holdings.Where(h => moving.All(a => a.Id != h.Id)).Concat(added).ToList();
            if (snapshot.Resolver().FindLoop() is null && (snapshot with { Holdings = after }).Resolver().FindLoop() is not null)
            {
                return Result.Failure<TransferPostsResultDto>(Error.Conflict("این انتقال حلقه‌ی مدیریتی می‌سازد."));
            }
        }

        for (var i = 0; i < moving.Count; i++)
        {
            moving[i].End(start);
            await repository.AddUserPositionAsync(added[i], cancellationToken);
        }

        if (!await SaveAppointmentsAsync(turn, cancellationToken))
        {
            return Result.Failure<TransferPostsResultDto>(ChangedMeanwhile());
        }

        if (start <= now)
        {
            if (taskPosts is not null)
            {
                await taskPosts.SyncHoldersAsync(tenantId, moving.Select(a => a.UnitPositionId).ToList(), cancellationToken);
            }

            await RehomeAsync(tenantId, [userId, request.ToUserId], cancellationToken);
        }

        return Result.Success(new TransferPostsResultDto(
            userId, request.ToUserId, start,
            await AppointmentDtosAsync(tenantId, snapshot, added, cancellationToken)));
    }
}
