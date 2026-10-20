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
/// stands in for it (Acting, null: nobody) and the posts that may (filled ones of its company).
/// </summary>
public sealed record VacantPostDto(
    Guid PostId,
    string Title,
    string Code,
    string Place,
    ActingPostDto? Acting,
    IReadOnlyList<SubstitutionTaskDto> Tasks,
    IReadOnlyList<ActingPostDto> Candidates);

public sealed record ActingPostDto(Guid PostId, string Title, string Code, string Place, Guid? HolderUserId, string? HolderName);

public sealed record SubstitutionTaskDto(Guid TaskId, string Title, DateOnly DueDate, string Status);

public sealed record SetActingRequest(Guid ActingPostId);

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
}

public sealed class SubstitutionService(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    IOrganizationUnitRepository units,
    TaskPostDirectory taskPosts,
    ICurrentUserContext currentUser,
    IUserDirectory users,
    IServiceProvider services) : ISubstitutionService
{
    public async Task<Result<SubstitutionInboxDto>> GetInboxAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var canManageAll = currentUser.HasPermission(OrgStructurePermissions.ManageSubstitution);
        if (!taskPosts.Enabled || currentUser.UserId is not { } me)
        {
            return Result.Success(new SubstitutionInboxDto(false, false, []));
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var rows = await repository.ListTaskPostsAsync(tenantId, null, state.Vacant.Select(p => p.PostId).ToList(), cancellationToken);
        var visible = state.Vacant.Where(post => canManageAll || state.SuperiorsOf(post.PostId).Contains(me)).ToList();
        var taskIds = rows.Where(row => visible.Any(post => post.PostId == row.PostId)).Select(row => row.TaskId).Distinct().ToList();
        var summaries = taskIds.Count == 0 || services.GetService<ITaskPeopleUpdater>() is not { } updater
            ? []
            : (await updater.GetSummariesAsync(tenantId, taskIds, cancellationToken)).Where(task => !task.IsDone).ToDictionary(task => task.TaskId);

        var names = await NamesAsync(state.Posts.Values.Select(p => p.HolderUserId), cancellationToken);
        ActingPostDto Ref(WorkPost post) => new(
            post.PostId, post.Title, post.Code, post.Place, post.HolderUserId,
            post.HolderUserId is { } holder ? names.GetValueOrDefault(holder) : null);

        var inbox = visible
            .Select(post =>
            {
                var tasks = rows.Where(row => row.PostId == post.PostId).Select(row => row.TaskId).Distinct()
                    .Where(summaries.ContainsKey).Select(id => summaries[id])
                    .OrderBy(task => task.DueDate)
                    .Select(task => new SubstitutionTaskDto(task.TaskId, task.Title, task.DueDate, task.Status))
                    .ToList();
                var acting = state.Actings.FirstOrDefault(a => a.VacantPostId == post.PostId);
                var company = state.CompanyOf(post.PostId);
                var candidates = state.Posts.Values
                    .Where(other => other.HolderUserId is not null && other.PostId != post.PostId && state.CompanyOf(other.PostId) == company)
                    .OrderBy(other => other.Place).ThenBy(other => other.Title)
                    .Select(Ref)
                    .ToList();
                return new VacantPostDto(
                    post.PostId, post.Title, post.Code, post.Place,
                    acting is not null && state.Posts.TryGetValue(acting.ActingPostId, out var by) ? Ref(by) : null,
                    tasks, candidates);
            })
            .Where(post => post.Tasks.Count > 0 || post.Acting is not null)
            .OrderBy(post => post.Place).ThenBy(post => post.Title)
            .ToList();
        return Result.Success(new SubstitutionInboxDto(true, canManageAll, inbox));
    }

    public async Task<Result> SetActingAsync(Guid tenantId, Guid vacantPostId, SetActingRequest request, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        if (CheckRights(state, vacantPostId) is { } refused)
        {
            return refused;
        }

        if (!state.Posts.TryGetValue(request.ActingPostId, out var acting) || acting.HolderUserId is null
            || request.ActingPostId == vacantPostId || state.CompanyOf(request.ActingPostId) != state.CompanyOf(vacantPostId))
        {
            return Result.Failure(Error.Validation("The post put in charge must be another post of the company that someone holds."));
        }

        var current = state.Actings.FirstOrDefault(a => a.VacantPostId == vacantPostId);
        if (current is null)
        {
            await repository.AddPostActingAsync(new PostActing(Guid.NewGuid(), tenantId, vacantPostId, request.ActingPostId), cancellationToken);
        }
        else
        {
            current.ActBy(request.ActingPostId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await taskPosts.SyncActingAsync(tenantId, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ClearActingAsync(Guid tenantId, Guid vacantPostId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        if (CheckRights(state, vacantPostId) is { } refused)
        {
            return refused;
        }

        if (state.Actings.FirstOrDefault(a => a.VacantPostId == vacantPostId) is not { } acting)
        {
            return Result.Failure(Error.NotFound("Nobody is in charge of this post."));
        }

        // Take the view back first, then forget the stand-in.
        acting.ActBy(vacantPostId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await taskPosts.SyncActingAsync(tenantId, cancellationToken);
        repository.RemovePostActing(acting);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Result? CheckRights(State state, Guid vacantPostId)
    {
        if (!taskPosts.Enabled)
        {
            return Result.Failure(Error.Validation("Tasks are not given to posts here."));
        }

        if (state.Vacant.All(post => post.PostId != vacantPostId))
        {
            return Result.Failure(Error.Validation("This post is not vacant."));
        }

        return currentUser.HasPermission(OrgStructurePermissions.ManageSubstitution)
            || (currentUser.UserId is { } me && state.SuperiorsOf(vacantPostId).Contains(me))
            ? null
            : Result.Failure(Error.Forbidden("Only the managers above a post may put someone in charge of it."));
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
        var unitList = await units.ListAsync(tenantId, cancellationToken);
        var companies = (await repository.ListCompaniesAsync(tenantId, cancellationToken)).ToDictionary(company => company.Id);
        var seats = (await repository.ListUnitPositionsAsync(tenantId, cancellationToken)).ToDictionary(seat => seat.Id);
        var positions = (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToDictionary(position => position.Id);
        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        var postSuperiors = PostSuperiors.Of(
            assignments.Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId)),
            seats.Values, await repository.ListUserPositionsAsync(tenantId, cancellationToken));
        var resolver = new HierarchyResolver(
            unitList.Select(unit => new UnitNode(unit.Id, null, unit.ManagerUserId)),
            (await repository.ListCompanyUnitsAsync(tenantId, cancellationToken)).ToDictionary(link => link.UnitId, link => link.CompanyId),
            companies.ToDictionary(company => company.Key, company => company.Value.ManagerUserId),
            assignments.Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId,
                postSuperiors.TryGetValue(a.UserId, out var superior) ? superior : null)));
        var posts = (await taskPosts.ListPostsAsync(tenantId, cancellationToken)).ToDictionary(post => post.PostId);
        return new State(
            posts,
            seats,
            unitList.ToDictionary(unit => unit.Id, unit => unit.ManagerUserId),
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

            var isHeadPost = seat.UnitId is null && Positions.GetValueOrDefault(seat.PositionId) is { ParentPositionId: null };
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
