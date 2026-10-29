using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// A node's manager post and the posts each node type gets (phase 6.1). Existing posts are never
/// changed by any of this: the post above is set only on posts made here (or added without one),
/// and running the same thing again makes nothing twice.
/// </summary>
public interface IOrgPostTemplateService
{
    /// <summary>Marks (or unmarks) a node's post as its manager post: at most one per node.</summary>
    Task<Result<UnitPositionDto>> SetManagerPostAsync(Guid tenantId, Guid unitPositionId, SetManagerPostRequest request, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<NodeTypePostDto>>> GetNodeTypePostsAsync(Guid tenantId, Guid nodeTypeId, CancellationToken cancellationToken);

    /// <summary>Replaces the posts a node type gets (nothing is made until they are applied).</summary>
    Task<Result<IReadOnlyList<NodeTypePostDto>>> SetNodeTypePostsAsync(Guid tenantId, Guid nodeTypeId, SetNodeTypePostsRequest request, CancellationToken cancellationToken);

    /// <summary>Makes the posts of the node type's pattern that a node does not have yet: on one node, or every node of a type.</summary>
    Task<Result<ApplyPostTemplatesResultDto>> ApplyPostTemplatesAsync(Guid tenantId, ApplyPostTemplatesRequest request, CancellationToken cancellationToken);
}

public sealed partial class OrgStructureService : IOrgPostTemplateService
{
    /// <summary>A node's manager post (the first by order when the data has more than one).</summary>
    private static UnitPosition? ManagerPostOf(IEnumerable<UnitPosition> seats, Guid unitId) =>
        seats.Where(seat => seat.UnitId == unitId && seat.IsManagerPost).OrderBy(seat => seat.SortOrder).FirstOrDefault();

    public async Task<Result<UnitPositionDto>> SetManagerPostAsync(Guid tenantId, Guid unitPositionId, SetManagerPostRequest request, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var seat = snapshot.Seats.FirstOrDefault(s => s.Id == unitPositionId);
        if (seat is null || !await IsMineAsync(tenantId, seat.CompanyId, cancellationToken))
        {
            return Result.Failure<UnitPositionDto>(Error.NotFound("پست پیدا نشد."));
        }

        if (seat.UnitId is not { } unitId)
        {
            return Result.Failure<UnitPositionDto>(Error.Validation("پست مستقیمِ شرکت پست مدیر گره نمی‌شود (پست رئیس شرکت جداست)."));
        }

        if (request.IsManagerPost && ManagerPostOf(snapshot.Seats, unitId) is { } other && other.Id != seat.Id)
        {
            return Result.Failure<UnitPositionDto>(Error.Conflict(
                $"این گره پست مدیر دارد ({snapshot.Positions.GetValueOrDefault(other.PositionId)?.Title} - {other.Code}); اول علامت آن را بردارید."));
        }

        // Only the mark: no post is put under it or taken from under it.
        seat.SetManagerPost(request.IsManagerPost);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var people = await PeopleAsync(snapshot.Holdings.Where(h => h.UnitPositionId == seat.Id).Select(h => (Guid?)h.UserId), cancellationToken);
        return Result.Success(ToDto(seat, snapshot, people));
    }

    // ---------------------------------------------------------------- the posts each node type gets

    public async Task<Result<IReadOnlyList<NodeTypePostDto>>> GetNodeTypePostsAsync(Guid tenantId, Guid nodeTypeId, CancellationToken cancellationToken)
    {
        if ((await repository.ListNodeTypesAsync(tenantId, cancellationToken)).All(type => type.Id != nodeTypeId))
        {
            return Result.Failure<IReadOnlyList<NodeTypePostDto>>(Error.NotFound("نوع گره پیدا نشد."));
        }

        var positions = (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToDictionary(p => p.Id);
        return Result.Success(PostsOf(await repository.ListNodeTypePostTemplatesAsync(tenantId, cancellationToken), nodeTypeId, positions));
    }

    public async Task<Result<IReadOnlyList<NodeTypePostDto>>> SetNodeTypePostsAsync(Guid tenantId, Guid nodeTypeId, SetNodeTypePostsRequest request, CancellationToken cancellationToken)
    {
        if ((await repository.ListNodeTypesAsync(tenantId, cancellationToken)).All(type => type.Id != nodeTypeId))
        {
            return Result.Failure<IReadOnlyList<NodeTypePostDto>>(Error.NotFound("نوع گره پیدا نشد."));
        }

        var wanted = request.Posts ?? [];
        var positions = (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToDictionary(p => p.Id);
        if (wanted.Any(post => !positions.ContainsKey(post.PositionId)))
        {
            return Result.Failure<IReadOnlyList<NodeTypePostDto>>(Error.Validation("سمتی که داده شد پیدا نشد."));
        }

        if (wanted.Count(post => post.IsManagerPost) > 1)
        {
            return Result.Failure<IReadOnlyList<NodeTypePostDto>>(Error.Validation("هر نوع گره حداکثر یک پست مدیر دارد."));
        }

        var all = await repository.ListNodeTypePostTemplatesAsync(tenantId, cancellationToken);
        foreach (var old in all.Where(t => t.NodeTypeId == nodeTypeId).ToList())
        {
            repository.RemoveNodeTypePostTemplate(old);
        }

        var made = new List<NodeTypePostTemplate>();
        for (var i = 0; i < wanted.Count; i++)
        {
            var template = new NodeTypePostTemplate(Guid.NewGuid(), tenantId, nodeTypeId, wanted[i].PositionId, wanted[i].IsManagerPost, i);
            await repository.AddNodeTypePostTemplateAsync(template, cancellationToken);
            made.Add(template);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(PostsOf(made, nodeTypeId, positions));
    }

    private static IReadOnlyList<NodeTypePostDto> PostsOf(IEnumerable<NodeTypePostTemplate> templates, Guid nodeTypeId, IReadOnlyDictionary<Guid, Position> positions) =>
        templates.Where(t => t.NodeTypeId == nodeTypeId).OrderBy(t => t.SortOrder)
            .Select(t => new NodeTypePostDto(t.PositionId, positions.GetValueOrDefault(t.PositionId)?.Title ?? string.Empty, t.IsManagerPost, t.SortOrder))
            .ToList();

    // ---------------------------------------------------------------- applying them

    public async Task<Result<ApplyPostTemplatesResultDto>> ApplyPostTemplatesAsync(Guid tenantId, ApplyPostTemplatesRequest request, CancellationToken cancellationToken)
    {
        if (request.NodeId is null == request.NodeTypeId is null)
        {
            return Result.Failure<ApplyPostTemplatesResultDto>(Error.Validation("یا یک گره بدهید یا یک نوع گره (نه هر دو)."));
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        List<Nexus.Organization.Domain.OrganizationUnit> nodes;
        if (request.NodeId is { } nodeId)
        {
            if (!snapshot.Units.TryGetValue(nodeId, out var node)
                || (snapshot.Graph.CompanyOfUnit(nodeId) is { } c && mine is not null && !mine.Contains(c)))
            {
                return Result.Failure<ApplyPostTemplatesResultDto>(Error.NotFound("گره پیدا نشد."));
            }

            nodes = [node];
        }
        else
        {
            nodes = snapshot.Units.Values
                .Where(unit => unit.NodeTypeId == request.NodeTypeId && unit.IsActive)
                .Where(unit => mine is null || snapshot.Graph.CompanyOfUnit(unit.Id) is not { } c || mine.Contains(c))
                .OrderBy(unit => unit.Name)
                .ToList();
        }

        var templates = await repository.ListNodeTypePostTemplatesAsync(tenantId, cancellationToken);
        var (made, skipped) = await MakeTemplatePostsAsync(tenantId, snapshot, nodes, templates, cancellationToken);
        if (made.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var empty = new Dictionary<Guid, NexusCore.Application.Identity.Interfaces.UserContact>();
        return Result.Success(new ApplyPostTemplatesResultDto(made.Select(seat => ToDto(seat, snapshot, empty)).ToList(), skipped));
    }

    /// <summary>
    /// Adds (not saved) the posts each node's type should have that it does not have yet. A post of
    /// a position the node already has is not made again; the manager post is made only when the
    /// node has none; new posts go under the node's manager post.
    /// </summary>
    private async Task<(List<UnitPosition> Made, List<SkippedPostDto> Skipped)> MakeTemplatePostsAsync(
        Guid tenantId, Snapshot snapshot, IReadOnlyList<Nexus.Organization.Domain.OrganizationUnit> nodes,
        IReadOnlyList<NodeTypePostTemplate> templates, CancellationToken cancellationToken, Dictionary<Guid, List<string>>? codes = null)
    {
        var made = new List<UnitPosition>();
        var skipped = new List<SkippedPostDto>();
        codes ??= [];
        foreach (var node in nodes)
        {
            var pattern = templates.Where(t => t.NodeTypeId == node.NodeTypeId).OrderBy(t => t.SortOrder).ToList();
            if (pattern.Count == 0)
            {
                continue;
            }

            if (snapshot.Graph.CompanyOfUnit(node.Id) is not { } companyId)
            {
                skipped.Add(new SkippedPostDto(node.Id, node.Name, null, "گره به شرکتی وصل نیست."));
                continue;
            }

            var seats = snapshot.Seats.Where(seat => seat.UnitId == node.Id).ToList();
            var manager = ManagerPostOf(seats, node.Id);
            var newHere = new List<UnitPosition>();
            var order = seats.Select(seat => seat.SortOrder).DefaultIfEmpty(-1).Max() + 1;
            foreach (var template in pattern)
            {
                var position = snapshot.Positions.GetValueOrDefault(template.PositionId);
                if (seats.Any(seat => seat.PositionId == template.PositionId))
                {
                    continue; // already there: nothing to do
                }

                if (position is not { IsActive: true })
                {
                    skipped.Add(new SkippedPostDto(node.Id, node.Name, position?.Title, "سمت پیدا نشد یا غیرفعال است."));
                    continue;
                }

                if (!snapshot.IsFor(position.Id, companyId))
                {
                    skipped.Add(new SkippedPostDto(node.Id, node.Name, position.Title, "این سمت برای شرکت این گره تعریف نشده است."));
                    continue;
                }

                var taken = codes.TryGetValue(companyId, out var list) ? list : codes[companyId] = [];
                var code = NextCode(snapshot, companyId, taken);
                taken.Add(code);
                var seat = new UnitPosition(Guid.NewGuid(), tenantId, companyId, node.Id, position.Id, order++, code);
                if (template.IsManagerPost && manager is null)
                {
                    seat.SetManagerPost(true);
                    manager = seat;
                }

                newHere.Add(seat);
            }

            foreach (var seat in newHere)
            {
                if (manager is not null && seat.Id != manager.Id)
                {
                    seat.PlaceUnder(manager.Id);
                }

                await repository.AddUnitPositionAsync(seat, cancellationToken);
            }

            made.AddRange(newHere);
        }

        return (made, skipped);
    }

    /// <summary>
    /// After a company is made from a template: its own posts (the head's...), then each node's posts
    /// from its type's pattern. Saved separately from the company; running it again makes nothing twice.
    /// </summary>
    private async Task<Result> MakeTemplateCompanyPostsAsync(Guid tenantId, Guid companyId, IReadOnlyList<CompanyPostToMake> companyPosts, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var order = snapshot.Seats.Where(seat => seat.CompanyId == companyId && seat.UnitId is null).Select(seat => seat.SortOrder).DefaultIfEmpty(-1).Max() + 1;
        var taken = new List<string>();
        var codes = new Dictionary<Guid, List<string>> { [companyId] = taken };
        foreach (var post in companyPosts)
        {
            if (snapshot.Seats.Any(seat => seat.CompanyId == companyId && seat.UnitId is null && seat.PositionId == post.PositionId))
            {
                continue;
            }

            var code = NextCode(snapshot, companyId, taken);
            taken.Add(code);
            var seat = new UnitPosition(Guid.NewGuid(), tenantId, companyId, null, post.PositionId, order++, code);
            seat.SetCompanyHead(post.IsCompanyHead || CompanyHeadPost.Is(seat, snapshot.Positions.GetValueOrDefault(post.PositionId), snapshot.IsFor(post.PositionId, companyId)));
            await repository.AddUnitPositionAsync(seat, cancellationToken);
        }

        var nodes = snapshot.Units.Values.Where(unit => snapshot.Graph.CompanyOfUnit(unit.Id) == companyId).OrderBy(unit => unit.Name).ToList();
        await MakeTemplatePostsAsync(tenantId, snapshot, nodes, await repository.ListNodeTypePostTemplatesAsync(tenantId, cancellationToken), cancellationToken, codes);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
