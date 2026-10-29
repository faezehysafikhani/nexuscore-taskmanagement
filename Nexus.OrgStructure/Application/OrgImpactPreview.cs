using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using Nexus.OrgStructure.Application.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// One proposed change. Kind: "move" (NodeId under ParentNodeId, or to the top of CompanyId / its
/// own company - as the move endpoint does it: a node put under another loses its company link),
/// "add" (a new empty node: Name, ParentNodeId or CompanyId), "manager" (NodeId gets ManagerUserId;
/// null: none), "unlink" (NodeId loses its company link), "link" (a secondary link from NodeId to
/// ToNodeId of LinkTypeId) or "unlink-link" (the secondary link LinkId goes).
/// </summary>
public sealed record ImpactChangeRequest(
    string Kind,
    Guid? NodeId = null,
    Guid? ParentNodeId = null,
    Guid? CompanyId = null,
    Guid? ManagerUserId = null,
    string? Name = null,
    Guid? ToNodeId = null,
    Guid? LinkTypeId = null,
    Guid? LinkId = null);

/// <summary>The changes, applied together in this order.</summary>
public sealed record ImpactPreviewRequest(IReadOnlyList<ImpactChangeRequest> Changes);

public sealed record ImpactPostDto(Guid PostId, string Title, string Code, string Place);

/// <summary>
/// What changes for one person: their manager; the people under them (at any depth); whom they may
/// no longer / now give tasks to (with LimitTaskAssignment on); the vacant posts whose substitution
/// inbox they see; whom they may add to their work teams; their extra managers (through links that
/// count as manager). Only what changes is listed.
/// </summary>
public sealed record ImpactPersonDto(
    PersonRef Person,
    bool ManagerChanged,
    PersonRef? ManagerBefore,
    PersonRef? ManagerAfter,
    IReadOnlyList<PersonRef> SubordinatesAdded,
    IReadOnlyList<PersonRef> SubordinatesRemoved,
    IReadOnlyList<PersonRef> CannotAssignAdded,
    IReadOnlyList<PersonRef> CannotAssignRemoved,
    IReadOnlyList<ImpactPostDto> SubstitutionAdded,
    IReadOnlyList<ImpactPostDto> SubstitutionRemoved,
    IReadOnlyList<PersonRef> TeamAdded,
    IReadOnlyList<PersonRef> TeamRemoved,
    IReadOnlyList<PersonRef>? ExtraManagersAdded = null,
    IReadOnlyList<PersonRef>? ExtraManagersRemoved = null);

/// <summary>PeopleAffected: how many people have any change. TaskLimitOn: whether «cannot give tasks to» was compared (LimitTaskAssignment).</summary>
public sealed record ImpactPreviewDto(int PeopleAffected, IReadOnlyList<ImpactPersonDto> People, bool TaskLimitOn);

/// <summary>
/// What a change to the structure would do to people, before it is made: the real rules (manager
/// chain, the task limit, work teams, the substitution inbox) run twice over the data held in
/// memory - as it is and as it would be (StructureOverlay). Nothing is written: the data the rules
/// see refuses every write.
/// </summary>
public interface IOrgImpactPreview
{
    Task<Result<ImpactPreviewDto>> PreviewAsync(Guid tenantId, ImpactPreviewRequest request, CancellationToken cancellationToken);
}

public sealed class OrgImpactPreview(
    IOrgStructureRepository repository,
    IOrganizationUnitRepository units,
    IUserDirectory users,
    IWorkTeamDirectory workTeams,
    IOptions<OrgStructureOptions> options,
    CompanyReachScope? companyReach = null,
    ICurrentUserContext? currentUser = null) : IOrgImpactPreview
{
    public async Task<Result<ImpactPreviewDto>> PreviewAsync(Guid tenantId, ImpactPreviewRequest request, CancellationToken cancellationToken)
    {
        if (request.Changes is not { Count: > 0 })
        {
            return Result.Failure<ImpactPreviewDto>(Error.Validation("تغییری برای پیش‌نمایش داده نشده است."));
        }

        var data = new ImpactData(repository, units, tenantId);
        var overlay = new StructureOverlay();
        var mine = companyReach is not null && currentUser?.UserId is { } me
            ? await companyReach.GetCompaniesAsync(tenantId, me, cancellationToken)
            : null;
        var before = await ViewAsync(data, new StructureOverlay(), cancellationToken);
        foreach (var change in request.Changes)
        {
            if (await ApplyAsync(data, overlay, change, mine, tenantId, cancellationToken) is { } problem)
            {
                return Result.Failure<ImpactPreviewDto>(problem);
            }
        }

        var after = await ViewAsync(data, overlay, cancellationToken);

        // The people of the structure, before and after (a new empty node adds nobody).
        var people = before.Managers.Keys.Concat(after.Managers.Keys).ToHashSet();
        // Everyone the result may name: the people, their managers, and whoever is in their lists.
        var named = new HashSet<Guid>(people);
        foreach (var view in new[] { before, after })
        {
            named.UnionWith(view.Managers.Values.OfType<Guid>());
            named.UnionWith(view.Subordinates.Values.SelectMany(ids => ids));
            named.UnionWith(view.Blocked.Values.SelectMany(ids => ids));
            named.UnionWith(view.Team.Values.OfType<HashSet<Guid>>().SelectMany(ids => ids));
            named.UnionWith(view.ExtraManagers.Values.SelectMany(ids => ids));
        }

        var directory = (await users.GetUsersAsync(named.ToList(), cancellationToken)).ToDictionary(user => user.Id);
        PersonRef Ref(Guid id) => directory.TryGetValue(id, out var user)
            ? new PersonRef(id, user.DisplayName, user.IsActive, user.AvatarUrl)
            : new PersonRef(id, id.ToString(), false);
        List<PersonRef> Refs(IEnumerable<Guid> ids) => ids.Select(Ref).OrderBy(person => person.DisplayName).ToList();

        var changed = new List<ImpactPersonDto>();
        foreach (var person in people)
        {
            var managerBefore = before.Managers.GetValueOrDefault(person);
            var managerAfter = after.Managers.GetValueOrDefault(person);
            var subsBefore = before.Subordinates.GetValueOrDefault(person) ?? [];
            var subsAfter = after.Subordinates.GetValueOrDefault(person) ?? [];
            var blockedBefore = before.Blocked.GetValueOrDefault(person) ?? [];
            var blockedAfter = after.Blocked.GetValueOrDefault(person) ?? [];
            var postsBefore = before.Supervised.GetValueOrDefault(person) ?? [];
            var postsAfter = after.Supervised.GetValueOrDefault(person) ?? [];
            var teamBefore = before.Team.GetValueOrDefault(person);
            var teamAfter = after.Team.GetValueOrDefault(person);
            var teamAdded = teamAfter is null || teamBefore is null ? [] : teamAfter.Except(teamBefore).ToList();
            var teamRemoved = teamAfter is null || teamBefore is null ? [] : teamBefore.Except(teamAfter).ToList();
            var extraBefore = before.ExtraManagers.GetValueOrDefault(person) ?? [];
            var extraAfter = after.ExtraManagers.GetValueOrDefault(person) ?? [];

            var dto = new ImpactPersonDto(
                Ref(person),
                managerBefore != managerAfter,
                managerBefore is { } mb ? Ref(mb) : null,
                managerAfter is { } ma ? Ref(ma) : null,
                Refs(subsAfter.Except(subsBefore)),
                Refs(subsBefore.Except(subsAfter)),
                Refs(blockedAfter.Except(blockedBefore)),
                Refs(blockedBefore.Except(blockedAfter)),
                postsAfter.Except(postsBefore).Select(id => after.Posts[id]).OrderBy(post => post.Title).ToList(),
                postsBefore.Except(postsAfter).Select(id => before.Posts[id]).OrderBy(post => post.Title).ToList(),
                Refs(teamAdded),
                Refs(teamRemoved),
                Refs(extraAfter.Except(extraBefore)),
                Refs(extraBefore.Except(extraAfter)));
            if (dto.ManagerChanged || dto.SubordinatesAdded.Count + dto.SubordinatesRemoved.Count + dto.CannotAssignAdded.Count
                + dto.CannotAssignRemoved.Count + dto.SubstitutionAdded.Count + dto.SubstitutionRemoved.Count
                + dto.TeamAdded.Count + dto.TeamRemoved.Count + dto.ExtraManagersAdded!.Count + dto.ExtraManagersRemoved!.Count > 0)
            {
                changed.Add(dto);
            }
        }

        return Result.Success(new ImpactPreviewDto(
            changed.Count,
            changed.OrderBy(person => person.Person.DisplayName).ToList(),
            options.Value.LimitTaskAssignment));
    }

    /// <summary>Lays one change over the data; an error when it cannot be made (unknown node, a loop...).</summary>
    private static async Task<Error?> ApplyAsync(
        ImpactData data, StructureOverlay overlay, ImpactChangeRequest change, IReadOnlySet<Guid>? mine, Guid tenantId, CancellationToken cancellationToken)
    {
        var view = data.Units(overlay);
        var all = (await view.ListAsync(tenantId, cancellationToken)).ToDictionary(unit => unit.Id);
        var graph = OrgGraph.Build(all.Values, await data.Structure(overlay).ListNodeTypesAsync(tenantId, cancellationToken),
            await data.Structure(overlay).ListCompanyUnitsAsync(tenantId, cancellationToken));
        bool Visible(Guid unitId) => mine is null || graph.CompanyOfUnit(unitId) is not { } company || mine.Contains(company);
        Error? Known(Guid? id, string what) =>
            id is { } nodeId && all.ContainsKey(nodeId) && Visible(nodeId) ? null : Error.NotFound($"{what} پیدا نشد.");

        switch (change.Kind?.Trim().ToLowerInvariant())
        {
            case "move":
                if (Known(change.NodeId, "گره") is { } missing)
                {
                    return missing;
                }

                var nodeId = change.NodeId!.Value;
                if (change.ParentNodeId is { } parentId)
                {
                    if (Known(parentId, "گره‌ی مقصد") is { } missingParent)
                    {
                        return missingParent;
                    }

                    if (graph.SubtreeOf(nodeId).Contains(parentId))
                    {
                        return Error.Validation("گره را نمی‌شود زیر خودش یا زیرمجموعه‌ی خودش برد.");
                    }

                    overlay.Parents[nodeId] = parentId;
                    overlay.Links[nodeId] = null;
                }
                else
                {
                    overlay.Parents[nodeId] = null;
                    overlay.Links[nodeId] = change.CompanyId ?? graph.CompanyOfUnit(nodeId);
                }

                return null;

            case "add":
                if (string.IsNullOrWhiteSpace(change.Name))
                {
                    return Error.Validation("نام گره‌ی تازه را وارد کنید.");
                }

                if (change.ParentNodeId is { } under && Known(under, "گره‌ی والد") is { } missingUnder)
                {
                    return missingUnder;
                }

                var unit = new OrganizationUnit(Guid.NewGuid(), tenantId, change.Name);
                unit.SetParent(change.ParentNodeId);
                overlay.NewUnits.Add(unit);
                if (change.ParentNodeId is null && change.CompanyId is { } company)
                {
                    overlay.Links[unit.Id] = company;
                }

                return null;

            case "manager":
                if (Known(change.NodeId, "گره") is { } missingNode)
                {
                    return missingNode;
                }

                overlay.Managers[change.NodeId!.Value] = change.ManagerUserId;
                return null;

            case "unlink":
                if (Known(change.NodeId, "گره") is { } missingLinked)
                {
                    return missingLinked;
                }

                overlay.Links[change.NodeId!.Value] = null;
                return null;

            case "link":
                if (Known(change.NodeId, "گره‌ی مبدأ") is { } missingFrom)
                {
                    return missingFrom;
                }

                if (Known(change.ToNodeId, "گره‌ی مقصد") is { } missingTo)
                {
                    return missingTo;
                }

                if (change.NodeId == change.ToNodeId)
                {
                    return Error.Validation("گره را نمی‌شود به خودش وصل کرد.");
                }

                var linkTypes = await data.Structure(overlay).ListLinkTypesAsync(tenantId, cancellationToken);
                if (change.LinkTypeId is not { } linkTypeId || !linkTypes.Any(type => type.Id == linkTypeId))
                {
                    return Error.NotFound("نوع رابطه پیدا نشد.");
                }

                overlay.AddedLinks.Add(new Domain.OrgLink(Guid.NewGuid(), tenantId, change.NodeId!.Value, change.ToNodeId!.Value, linkTypeId));
                return null;

            case "unlink-link":
                var existing = await data.Structure(overlay).ListOrgLinksAsync(tenantId, cancellationToken);
                if (change.LinkId is not { } linkId || existing.FirstOrDefault(link => link.Id == linkId) is not { } found
                    || !Visible(found.FromUnitId))
                {
                    return Error.NotFound("رابطه پیدا نشد.");
                }

                overlay.RemovedLinks.Add(linkId);
                return null;

            default:
                return Error.Validation($"نوع تغییر «{change.Kind}» شناخته نیست (move، add، manager، unlink، link، unlink-link).");
        }
    }

    private sealed record View(
        IReadOnlyDictionary<Guid, Guid?> Managers,
        IReadOnlyDictionary<Guid, HashSet<Guid>> Subordinates,
        IReadOnlyDictionary<Guid, HashSet<Guid>> Blocked,
        IReadOnlyDictionary<Guid, HashSet<Guid>?> Team,
        IReadOnlyDictionary<Guid, HashSet<Guid>> Supervised,
        IReadOnlyDictionary<Guid, ImpactPostDto> Posts,
        IReadOnlyDictionary<Guid, HashSet<Guid>> ExtraManagers);

    /// <summary>Everything compared, for every person, as the structure is seen through the overlay.</summary>
    private async Task<View> ViewAsync(ImpactData data, StructureOverlay overlay, CancellationToken cancellationToken)
    {
        var tenantId = data.TenantId;
        var structure = data.Structure(overlay);
        var unitView = data.Units(overlay);
        var nobody = new NobodySignedIn(tenantId);
        var noServices = new ServiceCollection().BuildServiceProvider();
        var noSaving = new NoSaving();
        var service = new OrgStructureService(structure, noSaving, unitView, users, workTeams);

        var assignments = await structure.ListAssignmentsAsync(tenantId, cancellationToken);
        var heads = (await structure.ListCompaniesAsync(tenantId, cancellationToken)).Select(company => company.ManagerUserId).OfType<Guid>();
        var people = assignments.Select(a => a.UserId).Concat(heads).Distinct().ToList();

        var managers = new Dictionary<Guid, Guid?>();
        var subordinates = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var person in people)
        {
            managers[person] = (await service.GetManagerAsync(tenantId, person, cancellationToken)).Value?.UserId;
            subordinates[person] = (await service.GetSubordinatesAsync(tenantId, person, true, cancellationToken)).Value?
                .Select(sub => sub.UserId).ToHashSet() ?? [];
        }

        var blocked = new Dictionary<Guid, HashSet<Guid>>();
        if (options.Value.LimitTaskAssignment)
        {
            var scope = new HierarchyWorkAssignmentScope(structure, unitView, nobody, options);
            foreach (var person in people)
            {
                blocked[person] = (await scope.GetLimitAsync(tenantId, person, cancellationToken))?.BlockedUserIds.ToHashSet() ?? [];
            }
        }

        var team = new Dictionary<Guid, HashSet<Guid>?>();
        var teamScope = new DeputyWorkTeamMemberScope(structure, unitView, nobody);
        foreach (var person in people)
        {
            team[person] = (await teamScope.GetLimitAsync(tenantId, person, cancellationToken))?.AllowedUserIds.ToHashSet();
        }

        var substitution = new SubstitutionService(
            structure, noSaving, unitView, new TaskPostDirectory(structure, noSaving, unitView, options, noServices), nobody, users, noServices);
        var vacant = await substitution.VacantPostSuperiorsAsync(tenantId, cancellationToken);
        var supervised = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var (postId, (_, superiors)) in vacant)
        {
            foreach (var superior in superiors)
            {
                (supervised.TryGetValue(superior, out var posts) ? posts : supervised[superior] = []).Add(postId);
            }
        }

        // Extra managers: only when some link counts as manager (none by default: nothing to compare).
        var extra = new Dictionary<Guid, HashSet<Guid>>();
        var (resolver, countingLinks) = await OrgNodeService.ResolverAsync(structure, unitView, tenantId, cancellationToken);
        if (countingLinks.Count > 0)
        {
            foreach (var person in people)
            {
                extra[person] = resolver.ExtraManagersOf(person, countingLinks).ToHashSet();
            }
        }

        return new View(managers, subordinates, blocked, team, supervised,
            vacant.ToDictionary(pair => pair.Key, pair => new ImpactPostDto(pair.Key, pair.Value.Post.Title, pair.Value.Post.Code, pair.Value.Post.Place)),
            extra);
    }

    /// <summary>No one signed in: nobody is let past a limit by their own permissions.</summary>
    private sealed class NobodySignedIn(Guid tenantId) : ICurrentUserContext
    {
        public Guid? UserId => null;
        public Guid? TenantId => tenantId;
        public string? Email => null;
        public string? IpAddress => null;
        public bool HasPermission(string permission) => false;
    }
}
