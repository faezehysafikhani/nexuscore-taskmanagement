using Microsoft.Extensions.DependencyInjection;
using Nexus.Organization.Application;
using Nexus.OrgStructure.Domain;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

public sealed record SubstitutionInboxDto(bool Enabled, bool CanManageAll, IReadOnlyList<VacantPostDto> Posts);

/// <summary>
/// A vacant post with open tasks: what the inbox shows of them (title, due date, status), who
/// stands in for it (Acting, null: nobody) and the posts that may (filled ones of its company,
/// but not those of the managers above it).
/// </summary>
public sealed record VacantPostDto(
    Guid PostId,
    string Title,
    string Code,
    string Place,
    ActingPostDto? Acting,
    IReadOnlyList<SubstitutionTaskDto> Tasks,
    IReadOnlyList<ActingPostDto> Candidates,
    Guid? HolderUserId = null,
    string? HolderName = null,
    DateTimeOffset? ActingFrom = null,
    DateTimeOffset? ActingUntil = null);

public sealed record ActingPostDto(Guid PostId, string Title, string Code, string Place, Guid? HolderUserId, string? HolderName);

public sealed record SubstitutionTaskDto(Guid TaskId, string Title, DateOnly DueDate, string Status);

/// <summary>
/// StartDate/EndDate: a stand-in for a set time (EndDate required for a post that has a holder);
/// without them it lasts while the post is vacant.
/// </summary>
public sealed record SetActingRequest(Guid ActingPostId, DateTimeOffset? StartDate = null, DateTimeOffset? EndDate = null);

/// <summary>
/// The substitution inbox (کارتابل جانشینی), where tasks are given to posts: the open tasks of
/// vacant posts, for the managers above each post and for holders of ManageSubstitution, who may
/// put another post in charge of a vacant one (سرپرستی) until someone takes it. The inbox shows
/// only titles, due dates and statuses; the stand-in only sees the tasks and comments.
/// </summary>
public interface ISubstitutionService
{
    Task<Result<SubstitutionInboxDto>> GetInboxAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result> SetActingAsync(Guid tenantId, Guid vacantPostId, SetActingRequest request, CancellationToken cancellationToken);
    Task<Result> ClearActingAsync(Guid tenantId, Guid vacantPostId, CancellationToken cancellationToken);

    /// <summary>One post (vacant or not) for «تعیین جانشین»: its stand-in and the posts that may stand in.</summary>
    Task<Result<VacantPostDto>> GetPostAsync(Guid tenantId, Guid postId, CancellationToken cancellationToken);
}

public sealed class SubstitutionService(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    IOrganizationUnitRepository units,
    TaskPostDirectory taskPosts,
    ICurrentUserContext currentUser,
    IUserDirectory users,
    IServiceProvider services,
    IOrgGraphProvider? graphs = null) : ISubstitutionService
{
    public async Task<Result<SubstitutionInboxDto>> GetInboxAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var canManageAll = currentUser.HasPermission(OrgStructurePermissions.ManageSubstitution);
        if (!taskPosts.Enabled || currentUser.UserId is not { } me)
        {
            return Result.Success(new SubstitutionInboxDto(false, false, []));
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        // Vacant posts, and posts with a holder that have a stand-in for a set time.
        var listed = state.Vacant.Concat(state.Posts.Values.Where(post => post.HolderUserId is not null
            && state.Actings.Any(a => a.VacantPostId == post.PostId))).ToList();
        var rows = await repository.ListTaskPostsAsync(tenantId, null, listed.Select(p => p.PostId).ToList(), cancellationToken);
        var visible = listed.Where(post => canManageAll || state.SuperiorsOf(post.PostId).Contains(me)).ToList();
        var taskIds = rows.Where(row => visible.Any(post => post.PostId == row.PostId)).Select(row => row.TaskId).Distinct().ToList();
        var summaries = taskIds.Count == 0 || services.GetService<ITaskPeopleUpdater>() is not { } updater
            ? []
            : (await updater.GetSummariesAsync(tenantId, taskIds, cancellationToken)).Where(task => !task.IsDone).ToDictionary(task => task.TaskId);

        var names = await NamesAsync(state.Posts.Values.Select(p => p.HolderUserId), cancellationToken);
        ActingPostDto Ref(WorkPost post) => new(
            post.PostId, post.Title, post.Code, post.Place, post.HolderUserId,
            post.HolderUserId is { } holder ? names.GetValueOrDefault(holder) : null);

        var inbox = visible
            .Select(post => ToDto(state, post, rows.Where(row => row.PostId == post.PostId).Select(row => row.TaskId).Distinct()
                .Where(summaries.ContainsKey).Select(id => summaries[id])
                .OrderBy(task => task.DueDate)
                .Select(task => new SubstitutionTaskDto(task.TaskId, task.Title, task.DueDate, task.Status))
                .ToList(), Ref))
            .Where(post => post.Tasks.Count > 0 || post.Acting is not null)
            .OrderBy(post => post.Place).ThenBy(post => post.Title)
            .ToList();
        return Result.Success(new SubstitutionInboxDto(true, canManageAll, inbox));
    }

    public async Task<Result<VacantPostDto>> GetPostAsync(Guid tenantId, Guid postId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        if (CheckRights(state, postId, dated: true) is { } refused)
        {
            return Result.Failure<VacantPostDto>(refused.Error);
        }

        var names = await NamesAsync(state.Posts.Values.Select(p => p.HolderUserId), cancellationToken);
        ActingPostDto Ref(WorkPost post) => new(
            post.PostId, post.Title, post.Code, post.Place, post.HolderUserId,
            post.HolderUserId is { } holder ? names.GetValueOrDefault(holder) : null);
        return Result.Success(ToDto(state, state.Posts[postId], [], Ref));
    }

    /// <summary>A post with its stand-in and the posts that may stand in for it.</summary>
    private static VacantPostDto ToDto(State state, WorkPost post, IReadOnlyList<SubstitutionTaskDto> tasks, Func<WorkPost, ActingPostDto> toRef)
    {
        var acting = state.Actings.FirstOrDefault(a => a.VacantPostId == post.PostId);
        var candidates = state.Posts.Values
            .Where(other => state.MayStandIn(post.PostId, other))
            .OrderBy(other => other.Place).ThenBy(other => other.Title)
            .Select(toRef)
            .ToList();
        var holder = post.HolderUserId is not null ? toRef(post).HolderName : null;
        return new VacantPostDto(
            post.PostId, post.Title, post.Code, post.Place,
            acting is not null && state.Posts.TryGetValue(acting.ActingPostId, out var by) ? toRef(by) : null,
            tasks, candidates, post.HolderUserId, holder, acting?.StartDate, acting?.EndDate);
    }

    public async Task<Result> SetActingAsync(Guid tenantId, Guid vacantPostId, SetActingRequest request, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        var dated = request.EndDate is not null;
        if (CheckRights(state, vacantPostId, dated) is { } refused)
        {
            return refused;
        }

        if (request.StartDate is { } from && request.EndDate is { } until && until <= from)
        {
            return Result.Failure(Error.Validation("پایان جانشینی باید بعد از شروع آن باشد."));
        }

        if (request.EndDate is { } end && end <= DateTimeOffset.UtcNow)
        {
            return Result.Failure(Error.Validation("پایان جانشینی نباید گذشته باشد."));
        }

        if (request.StartDate is not null && !dated)
        {
            return Result.Failure(Error.Validation("برای جانشینی از یک تاریخ، تاریخ پایان را هم بدهید."));
        }

        if (!state.Posts.TryGetValue(request.ActingPostId, out var acting) || acting.HolderUserId is not { } actingHolder
            || request.ActingPostId == vacantPostId || state.CompanyOf(request.ActingPostId) != state.CompanyOf(vacantPostId))
        {
            return Result.Failure(Error.Validation("پستی که سرپرست می‌شود باید پست دیگری از همین شرکت باشد که کسی آن را دارد."));
        }

        if (state.Posts.GetValueOrDefault(vacantPostId)?.HolderUserId == actingHolder)
        {
            return Result.Failure(Error.Validation("دارنده‌ی خود این پست نمی‌تواند جانشین آن باشد."));
        }

        // Only a post of the same unit (or a unit below it) may stand in.
        if (!state.InPlaceOf(vacantPostId, request.ActingPostId))
        {
            return Result.Failure(Error.Validation("جانشین باید از همین واحد (یا زیرمجموعه‌ی آن) باشد."));
        }

        // Not one of the managers above the vacant post: a stand-in is someone at its level or below.
        if (state.SuperiorsOf(vacantPostId).Contains(actingHolder))
        {
            return Result.Failure(Error.Validation("مدیرِ بالای پست خالی را نمی‌شود سرپرست همان پست کرد."));
        }

        var current = state.Actings.FirstOrDefault(a => a.VacantPostId == vacantPostId);
        if (current is null)
        {
            current = new PostActing(Guid.NewGuid(), tenantId, vacantPostId, request.ActingPostId);
            await repository.AddPostActingAsync(current, cancellationToken);
        }
        else
        {
            current.ActBy(request.ActingPostId);
        }

        current.SetPeriod(dated ? request.StartDate : null, request.EndDate);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await taskPosts.SyncActingAsync(tenantId, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ClearActingAsync(Guid tenantId, Guid vacantPostId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        if (CheckRights(state, vacantPostId, dated: true) is { } refused)
        {
            return refused;
        }

        if (state.Actings.FirstOrDefault(a => a.VacantPostId == vacantPostId) is not { } acting)
        {
            return Result.Failure(Error.NotFound("کسی سرپرست این پست نیست."));
        }

        // Ended now: the sync takes the view back and forgets the stand-in (the post may have a holder).
        acting.SetPeriod(null, DateTimeOffset.UtcNow.AddTicks(-1));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await taskPosts.SyncActingAsync(tenantId, cancellationToken);
        return Result.Success();
    }

    /// <summary>Who may set a post's stand-in. A post with a holder only for a set time (<paramref name="dated"/>).</summary>
    private Result? CheckRights(State state, Guid vacantPostId, bool dated = false)
    {
        if (!taskPosts.Enabled)
        {
            return Result.Failure(Error.Validation("در اینجا وظیفه به پست داده نمی‌شود."));
        }

        if (!state.Posts.ContainsKey(vacantPostId))
        {
            return Result.Failure(Error.NotFound("پست پیدا نشد."));
        }

        if (!dated && state.Vacant.All(post => post.PostId != vacantPostId))
        {
            return Result.Failure(Error.Validation("این پست مسئول دارد؛ برای جانشینی‌اش تاریخ شروع و پایان بدهید."));
        }

        return currentUser.HasPermission(OrgStructurePermissions.ManageSubstitution)
            || (currentUser.UserId is { } me && state.SuperiorsOf(vacantPostId).Contains(me))
            ? null
            : Result.Failure(Error.Forbidden("فقط مدیرهای بالای یک پست می‌توانند برایش سرپرست بگذارند."));
    }

    /// <summary>For the impact preview: each vacant post and the managers above it (whose inbox shows it).</summary>
    internal async Task<IReadOnlyDictionary<Guid, (WorkPost Post, IReadOnlyList<Guid> Superiors)>> VacantPostSuperiorsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        return state.Vacant.ToDictionary(post => post.PostId, post => (post, state.SuperiorsOf(post.PostId)));
    }

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.OfType<Guid>().Distinct().ToList();
        return distinct.Count == 0
            ? new Dictionary<Guid, string>()
            : (await users.GetUsersAsync(distinct, cancellationToken)).ToDictionary(user => user.Id, user => user.DisplayName);
    }

    private async Task<State> LoadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var graph = await (graphs ?? new OrgGraphProvider(units, repository)).GetAsync(tenantId, cancellationToken);
        var companies = (await repository.ListCompaniesAsync(tenantId, cancellationToken)).ToDictionary(company => company.Id);
        var seats = (await repository.ListUnitPositionsAsync(tenantId, cancellationToken)).ToDictionary(seat => seat.Id);
        var positions = (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToDictionary(position => position.Id);
        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        var postSuperiors = PostSuperiors.Of(
            assignments.Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId)),
            seats.Values, await repository.ListUserPositionsAsync(tenantId, cancellationToken));
        var resolver = new HierarchyResolver(
            graph,
            companies.ToDictionary(company => company.Key, company => company.Value.ManagerUserId),
            assignments.Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId,
                postSuperiors.TryGetValue(a.UserId, out var superior) ? superior : null)));
        var posts = (await taskPosts.ListPostsAsync(tenantId, cancellationToken)).ToDictionary(post => post.PostId);
        return new State(
            posts,
            seats,
            graph.Nodes.ToDictionary(node => node.Id, node => node.ManagerUserId),
            companies,
            positions,
            resolver,
            await repository.ListPostActingsAsync(tenantId, cancellationToken));
    }

    private sealed record State(
        IReadOnlyDictionary<Guid, WorkPost> Posts,
        IReadOnlyDictionary<Guid, UnitPosition> Seats,
        IReadOnlyDictionary<Guid, Guid?> UnitManagers,
        IReadOnlyDictionary<Guid, Company> Companies,
        IReadOnlyDictionary<Guid, Position> Positions,
        HierarchyResolver Resolver,
        IReadOnlyList<PostActing> Actings)
    {
        public IEnumerable<WorkPost> Vacant => Posts.Values.Where(post => post.HolderUserId is null);

        public Guid? CompanyOf(Guid postId) => Seats.GetValueOrDefault(postId)?.CompanyId;

        /// <summary>
        /// Whether <paramref name="other"/> may stand in for the post: someone holds it, it is in the
        /// post's unit or below (a post directly in the company: the company's), and its holder is
        /// neither above the post nor the post's own holder.
        /// </summary>
        public bool MayStandIn(Guid postId, WorkPost other)
        {
            if (other.HolderUserId is not { } holder || other.PostId == postId || CompanyOf(other.PostId) != CompanyOf(postId)
                || Posts.GetValueOrDefault(postId)?.HolderUserId == holder)
            {
                return false;
            }

            return InPlaceOf(postId, other.PostId) && !SuperiorsOf(postId).Contains(holder);
        }

        /// <summary>Whether the other post is in the post's unit or a unit below it (directly in the company: anywhere in it).</summary>
        public bool InPlaceOf(Guid postId, Guid otherPostId)
        {
            if (Seats.GetValueOrDefault(postId) is not { } seat || Seats.GetValueOrDefault(otherPostId) is not { } other)
            {
                return false;
            }

            return seat.UnitId is not { } unitId || (other.UnitId is { } otherUnit && Resolver.SubtreeOf(unitId).Contains(otherUnit));
        }

        /// <summary>
        /// The managers above a post: the holder of the nearest post above it that someone holds,
        /// else its unit's manager (for a post directly in the company, or a unit with no manager:
        /// the company's head), and their managers in turn. The head's own post has only those
        /// above the head.
        /// </summary>
        public IReadOnlyList<Guid> SuperiorsOf(Guid postId)
        {
            if (Seats.GetValueOrDefault(postId) is not { } seat)
            {
                return [];
            }

            var visited = new HashSet<Guid> { seat.Id };
            for (var parent = seat.ParentPostId; parent is { } parentId && visited.Add(parentId) && Seats.TryGetValue(parentId, out var above); parent = above.ParentPostId)
            {
                if (Posts.GetValueOrDefault(parentId)?.HolderUserId is { } holder)
                {
                    return [holder, .. Resolver.ManagementChainOf(holder)];
                }
            }

            var isHeadPost = CompanyHeadPost.Is(seat, Positions.GetValueOrDefault(seat.PositionId));
            var first = seat.UnitId is { } unitId ? UnitManagers.GetValueOrDefault(unitId) : null;
            first ??= isHeadPost ? null : Companies.GetValueOrDefault(seat.CompanyId)?.ManagerUserId;
            if (first is not { } manager)
            {
                return [];
            }

            return [manager, .. Resolver.ManagementChainOf(manager)];
        }
    }
}
