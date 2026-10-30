using Nexus.Organization.Domain;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>Secondary links (phase 4): their types, the automatic rules, the links themselves, and a company's screen level.</summary>
public interface IOrgLinkService
{
    Task<Result<IReadOnlyList<LinkTypeDto>>> ListLinkTypesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<LinkTypeDto>> CreateLinkTypeAsync(Guid tenantId, CreateLinkTypeRequest request, CancellationToken cancellationToken);
    Task<Result<LinkTypeDto>> UpdateLinkTypeAsync(Guid tenantId, Guid id, UpdateLinkTypeRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteLinkTypeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<AutoLinkRuleDto>>> ListAutoLinkRulesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<AutoLinkRuleDto>>> SetAutoLinkRulesAsync(Guid tenantId, SetAutoLinkRulesRequest request, CancellationToken cancellationToken);
    Task<Result<AutoLinkPlanDto>> PlanAutoLinksAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<OrgLinkDto>>> ApplyAutoLinksAsync(Guid tenantId, ApplyAutoLinksRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<OrgLinkDto>>> ListLinksAsync(Guid tenantId, Guid? nodeId, CancellationToken cancellationToken);
    Task<Result<OrgLinkDto>> CreateLinkAsync(Guid tenantId, CreateLinkRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteLinkAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<Result> SetCompanyUiLevelAsync(Guid tenantId, Guid companyId, SetUiLevelRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<PersonRef>>> GetExtraManagersAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}

public sealed partial class OrgNodeService : IOrgLinkService
{
    private static readonly string[] LineStyles = ["solid", "dotted", "dashed"];

    // ---------------------------------------------------------------- link types

    public async Task<Result<IReadOnlyList<LinkTypeDto>>> ListLinkTypesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<LinkTypeDto>>(state.LinkTypes.Values.OrderBy(type => type.Name).Select(type => ToDto(state, type)).ToList());
    }

    public async Task<Result<LinkTypeDto>> CreateLinkTypeAsync(Guid tenantId, CreateLinkTypeRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var style = request.LineStyle?.Trim().ToLowerInvariant() ?? string.Empty;
        if (LinkTypeProblem(name, style, request.CountsAsManager) is { } invalid)
        {
            return Result.Failure<LinkTypeDto>(invalid);
        }

        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<LinkTypeDto>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        if (state.LinkTypes.Values.Any(type => string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<LinkTypeDto>(Error.Conflict($"نوع رابطه‌ای با نام «{name}» وجود دارد."));
        }

        var code = request.Code?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code))
        {
            var n = state.LinkTypes.Count + 1;
            while (state.LinkTypes.Values.Any(type => type.Code == $"link-{n}"))
            {
                n++;
            }

            code = $"link-{n}";
        }
        else if (!CodePattern().IsMatch(code) || code.Length > 40)
        {
            return Result.Failure<LinkTypeDto>(Error.Validation("کد نوع رابطه فقط حروف کوچک لاتین، رقم و خط تیره است (حداکثر ۴۰)."));
        }
        else if (state.LinkTypes.Values.Any(type => type.Code == code))
        {
            return Result.Failure<LinkTypeDto>(Error.Conflict($"نوع رابطه‌ای با کد «{code}» وجود دارد."));
        }

        var linkType = new LinkType(Guid.NewGuid(), tenantId, code, name, request.CountsAsManager, style);
        await repository.AddLinkTypeAsync(linkType, cancellationToken);
        await LogAsync(tenantId, "LinkType", linkType.Id, "create", new { code, name, style, request.CountsAsManager }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success(ToDto(state, linkType));
    }

    public async Task<Result<LinkTypeDto>> UpdateLinkTypeAsync(Guid tenantId, Guid id, UpdateLinkTypeRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var style = request.LineStyle?.Trim().ToLowerInvariant() ?? string.Empty;
        if (LinkTypeProblem(name, style, request.CountsAsManager) is { } invalid)
        {
            return Result.Failure<LinkTypeDto>(invalid);
        }

        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<LinkTypeDto>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        if (!state.LinkTypes.TryGetValue(id, out var linkType))
        {
            return Result.Failure<LinkTypeDto>(Error.NotFound("نوع رابطه پیدا نشد."));
        }

        if (state.LinkTypes.Values.Any(type => type.Id != id && string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<LinkTypeDto>(Error.Conflict($"نوع رابطه‌ای با نام «{name}» وجود دارد."));
        }

        var before = new { linkType.Name, linkType.LineStyle, linkType.CountsAsManager, linkType.IsActive };
        linkType.Update(name, request.CountsAsManager, style, request.IsActive);
        await LogAsync(tenantId, "LinkType", id, "update", new { before, after = new { name, style, request.CountsAsManager, request.IsActive } }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success(ToDto(state, linkType));
    }

    public async Task<Result> DeleteLinkTypeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        if (!state.LinkTypes.TryGetValue(id, out var linkType))
        {
            return Result.Failure(Error.NotFound("نوع رابطه پیدا نشد."));
        }

        var used = state.OrgLinks.Count(link => link.LinkTypeId == id);
        if (used > 0 || state.AutoLinkRules.Any(rule => rule.LinkTypeId == id))
        {
            return Result.Failure(Error.Conflict(used > 0
                ? $"{FaDigits.Of(used)} رابطه از نوع «{linkType.Name}» است؛ به‌جای حذف، آن را غیرفعال کنید."
                : $"نوع رابطه‌ی «{linkType.Name}» در قاعده‌های خودکار به‌کار رفته است."));
        }

        repository.RemoveLinkType(linkType);
        await LogAsync(tenantId, "LinkType", id, "delete", new { linkType.Code, linkType.Name }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success();
    }

    private static Error? LinkTypeProblem(string name, string style, bool countsAsManager) =>
        name.Length == 0 ? Error.Validation("نام نوع رابطه را وارد کنید.")
        : name.Length > MaxTypeNameLength ? Error.Validation($"نام نوع رابطه حداکثر {FaDigits.Of(MaxTypeNameLength)} نویسه است.")
        : !LineStyles.Contains(style) ? Error.Validation("سبک خط یکی از solid، dotted یا dashed است.")
        : style == "dotted" && countsAsManager ? Error.Validation("خط نقطه‌چین هیچ‌وقت مدیر حساب نمی‌شود؛ برای رابطه‌ای که مدیر حساب شود خط ممتد یا خط‌چین بگذارید.")
        : null;

    // ---------------------------------------------------------------- automatic rules

    public async Task<Result<IReadOnlyList<AutoLinkRuleDto>>> ListAutoLinkRulesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<AutoLinkRuleDto>>(state.AutoLinkRules
            .Select(rule => new AutoLinkRuleDto(rule.SourceNodeTypeId, rule.TargetNodeTypeId, rule.LinkTypeId)).ToList());
    }

    /// <summary>Replaces the rules (one per source and target type). Existing links are not touched.</summary>
    public async Task<Result<IReadOnlyList<AutoLinkRuleDto>>> SetAutoLinkRulesAsync(Guid tenantId, SetAutoLinkRulesRequest request, CancellationToken cancellationToken)
    {
        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<IReadOnlyList<AutoLinkRuleDto>>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var wanted = (request.Rules ?? []).Distinct().ToList();
        if (wanted.Any(rule => !state.NodeTypes.ContainsKey(rule.SourceNodeTypeId) || !state.NodeTypes.ContainsKey(rule.TargetNodeTypeId)
                               || !state.LinkTypes.ContainsKey(rule.LinkTypeId)))
        {
            return Result.Failure<IReadOnlyList<AutoLinkRuleDto>>(Error.Validation("قاعده به نوع گره یا نوع رابطه‌ای اشاره می‌کند که وجود ندارد."));
        }

        if (wanted.GroupBy(rule => (rule.SourceNodeTypeId, rule.TargetNodeTypeId)).Any(group => group.Count() > 1))
        {
            return Result.Failure<IReadOnlyList<AutoLinkRuleDto>>(Error.Validation("برای هر جفت نوع مبدأ و مقصد فقط یک قاعده‌ی خودکار ممکن است."));
        }

        if (wanted.Any(rule => rule.SourceNodeTypeId == rule.TargetNodeTypeId))
        {
            return Result.Failure<IReadOnlyList<AutoLinkRuleDto>>(Error.Validation("مبدأ و مقصد قاعده‌ی خودکار نباید یک نوع باشند."));
        }

        var current = state.AutoLinkRules.ToList();
        foreach (var rule in current.Where(rule => !wanted.Contains(new AutoLinkRuleDto(rule.SourceNodeTypeId, rule.TargetNodeTypeId, rule.LinkTypeId))))
        {
            repository.RemoveAutoLinkRule(rule);
        }

        foreach (var rule in wanted.Where(rule => !current.Any(c => c.SourceNodeTypeId == rule.SourceNodeTypeId && c.TargetNodeTypeId == rule.TargetNodeTypeId && c.LinkTypeId == rule.LinkTypeId)))
        {
            await repository.AddAutoLinkRuleAsync(new AutoLinkRule(Guid.NewGuid(), tenantId, rule.SourceNodeTypeId, rule.TargetNodeTypeId, rule.LinkTypeId), cancellationToken);
        }

        await LogAsync(tenantId, "AutoLinkRules", tenantId, "set",
            new { before = current.Select(rule => new { rule.SourceNodeTypeId, rule.TargetNodeTypeId, rule.LinkTypeId }), after = wanted }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success<IReadOnlyList<AutoLinkRuleDto>>(wanted);
    }

    /// <summary>
    /// What the rules would add to a node: per rule of its type, the active nodes of the target type
    /// in its company (and not already linked that way). One: linked; several: a choice; none: nothing.
    /// </summary>
    private static (List<ProposedLinkDto> Links, List<AutoLinkChoiceDto> Choices) AutoLinksFor(NodeState state, OrganizationUnit unit, Guid? companyId)
    {
        var links = new List<ProposedLinkDto>();
        var choices = new List<AutoLinkChoiceDto>();
        if (unit.NodeTypeId is not { } typeId || !unit.IsActive)
        {
            return (links, choices);
        }

        foreach (var rule in state.AutoLinkRules.Where(rule => rule.SourceNodeTypeId == typeId))
        {
            if (!state.LinkTypes.TryGetValue(rule.LinkTypeId, out var linkType) || !linkType.IsActive)
            {
                continue;
            }

            var targets = state.Units.Values
                .Where(other => other.Id != unit.Id && other.IsActive && other.NodeTypeId == rule.TargetNodeTypeId
                                && state.Graph.CompanyOfUnit(other.Id) == companyId)
                .Where(other => !state.OrgLinks.Any(link => link.FromUnitId == unit.Id && link.ToUnitId == other.Id && link.LinkTypeId == rule.LinkTypeId))
                .OrderBy(other => other.Name)
                .ToList();
            if (targets.Count == 1)
            {
                links.Add(new ProposedLinkDto(unit.Id, unit.Name, targets[0].Id, targets[0].Name, linkType.Id, linkType.Name));
            }
            else if (targets.Count > 1)
            {
                choices.Add(new AutoLinkChoiceDto(unit.Id, unit.Name, linkType.Id, linkType.Name,
                    targets.Select(target => new NodePathItemDto(target.Id, target.Name)).ToList()));
            }
        }

        return (links, choices);
    }

    public async Task<Result<AutoLinkPlanDto>> PlanAutoLinksAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        return Result.Success(Plan(state, await MyCompaniesAsync(tenantId, cancellationToken)));
    }

    private static AutoLinkPlanDto Plan(NodeState state, IReadOnlySet<Guid>? mine)
    {
        var links = new List<ProposedLinkDto>();
        var ambiguous = new List<AutoLinkChoiceDto>();
        foreach (var unit in state.Units.Values.Where(unit => Sees(state, mine, unit.Id)).OrderBy(unit => unit.Name))
        {
            var (some, choices) = AutoLinksFor(state, unit, state.Graph.CompanyOfUnit(unit.Id));
            links.AddRange(some);
            ambiguous.AddRange(choices);
        }

        return new AutoLinkPlanDto(links, ambiguous);
    }

    /// <summary>Adds links of the automatic rules to existing nodes - only those the plan (as it is now) proposes.</summary>
    public async Task<Result<IReadOnlyList<OrgLinkDto>>> ApplyAutoLinksAsync(Guid tenantId, ApplyAutoLinksRequest request, CancellationToken cancellationToken)
    {
        if (request.Links is not { Count: > 0 })
        {
            return Result.Failure<IReadOnlyList<OrgLinkDto>>(Error.Validation("رابطه‌ای برای اعمال انتخاب نشده است."));
        }

        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<IReadOnlyList<OrgLinkDto>>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var plan = Plan(state, await MyCompaniesAsync(tenantId, cancellationToken)).Links
            .Select(link => (link.FromNodeId, link.ToNodeId, link.LinkTypeId)).ToHashSet();
        if (request.Links.Any(link => !plan.Contains((link.FromNodeId, link.ToNodeId, link.LinkTypeId))))
        {
            return Result.Failure<IReadOnlyList<OrgLinkDto>>(Error.Conflict("ساختار از زمان پیش‌نمایش تغییر کرده است؛ دوباره پیش‌نمایش بگیرید."));
        }

        var created = new List<OrgLink>();
        foreach (var link in request.Links.Distinct())
        {
            var orgLink = new OrgLink(Guid.NewGuid(), tenantId, link.FromNodeId, link.ToNodeId, link.LinkTypeId);
            await repository.AddOrgLinkAsync(orgLink, cancellationToken);
            created.Add(orgLink);
        }

        await LogAsync(tenantId, "AutoLinkRules", tenantId, "apply", new { links = request.Links }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success<IReadOnlyList<OrgLinkDto>>(created.Select(link => ToDto(state, link)).ToList());
    }

    // ---------------------------------------------------------------- links

    public async Task<Result<IReadOnlyList<OrgLinkDto>>> ListLinksAsync(Guid tenantId, Guid? nodeId, CancellationToken cancellationToken)
    {
        var state = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<OrgLinkDto>>(state.OrgLinks
            .Where(link => state.Units.ContainsKey(link.FromUnitId) && state.Units.ContainsKey(link.ToUnitId) && state.LinkTypes.ContainsKey(link.LinkTypeId))
            .Where(link => Sees(state, mine, link.FromUnitId) && Sees(state, mine, link.ToUnitId))
            .Where(link => nodeId is null || link.FromUnitId == nodeId || link.ToUnitId == nodeId)
            .Select(link => ToDto(state, link))
            .OrderBy(link => link.FromName).ThenBy(link => link.ToName)
            .ToList());
    }

    public async Task<Result<OrgLinkDto>> CreateLinkAsync(Guid tenantId, CreateLinkRequest request, CancellationToken cancellationToken)
    {
        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<OrgLinkDto>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        if (!state.Units.TryGetValue(request.FromNodeId, out var from) || !Sees(state, mine, from.Id)
            || !state.Units.TryGetValue(request.ToNodeId, out var to) || !Sees(state, mine, to.Id))
        {
            return Result.Failure<OrgLinkDto>(NodeNotFound());
        }

        if (!state.LinkTypes.TryGetValue(request.LinkTypeId, out var linkType) || !linkType.IsActive)
        {
            return Result.Failure<OrgLinkDto>(Error.Validation("نوع رابطه پیدا نشد یا غیرفعال است."));
        }

        if (from.Id == to.Id)
        {
            return Result.Failure<OrgLinkDto>(Error.Validation("گره را نمی‌شود به خودش وصل کرد."));
        }

        if (state.Graph.CompanyOfUnit(from.Id) != state.Graph.CompanyOfUnit(to.Id))
        {
            return Result.Failure<OrgLinkDto>(Error.Validation("رابطه‌ی فرعی فقط میان گره‌های یک شرکت است."));
        }

        if (state.OrgLinks.Any(link => link.FromUnitId == from.Id && link.ToUnitId == to.Id && link.LinkTypeId == linkType.Id))
        {
            return Result.Failure<OrgLinkDto>(Error.Conflict("این رابطه همین حالا وجود دارد."));
        }

        var orgLink = new OrgLink(Guid.NewGuid(), tenantId, from.Id, to.Id, linkType.Id);
        await repository.AddOrgLinkAsync(orgLink, cancellationToken);
        await LogAsync(tenantId, "OrgLink", orgLink.Id, "create", new { from = from.Id, to = to.Id, type = linkType.Id }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success(ToDto(state, orgLink));
    }

    public async Task<Result> DeleteLinkAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        if (state.OrgLinks.FirstOrDefault(link => link.Id == id) is not { } orgLink || !Sees(state, mine, orgLink.FromUnitId))
        {
            return Result.Failure(Error.NotFound("رابطه پیدا نشد."));
        }

        repository.RemoveOrgLink(orgLink);
        await LogAsync(tenantId, "OrgLink", id, "delete", new { from = orgLink.FromUnitId, to = orgLink.ToUnitId, type = orgLink.LinkTypeId }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success();
    }

    // ---------------------------------------------------------------- the company's screen level and extra managers

    public async Task<Result> SetCompanyUiLevelAsync(Guid tenantId, Guid companyId, SetUiLevelRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<OrgUiLevel>(request.UiLevel?.Trim(), ignoreCase: true, out var level) || !Enum.IsDefined(level))
        {
            return Result.Failure(Error.Validation("سطح نمایش یکی از simple، normal یا advanced است."));
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        if (!state.Companies.TryGetValue(companyId, out var company) || (mine is not null && !mine.Contains(companyId)))
        {
            return Result.Failure(Error.NotFound("شرکت پیدا نشد."));
        }

        var before = company.UiLevel;
        company.SetUiLevel(level);
        await LogAsync(tenantId, "Company", companyId, "ui-level", new { before = before.ToString(), after = level.ToString() }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>The managers someone has through links that count as manager (none when no link counts: the default).</summary>
    public async Task<Result<IReadOnlyList<PersonRef>>> GetExtraManagersAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var resolver = await ResolverAsync(repository, units, tenantId, cancellationToken);
        var managers = resolver.Resolver.ExtraManagersOf(userId, resolver.CountingLinks);
        var people = (await users.GetUsersAsync(managers, cancellationToken)).ToDictionary(user => user.Id);
        return Result.Success<IReadOnlyList<PersonRef>>(managers
            .Select(id => people.TryGetValue(id, out var user) ? new PersonRef(id, user.DisplayName, user.IsActive, user.AvatarUrl) : new PersonRef(id, id.ToString(), false))
            .ToList());
    }

    /// <summary>The reporting structure (as the other services build it) and the links that count as manager.</summary>
    internal static async Task<(HierarchyResolver Resolver, ILookup<Guid, Guid> CountingLinks)> ResolverAsync(
        IOrgStructureRepository repository, Nexus.Organization.Application.IOrganizationUnitRepository units, Guid tenantId, CancellationToken cancellationToken)
    {
        var graph = await new OrgGraphProvider(units, repository).GetAsync(tenantId, cancellationToken);
        var companies = await repository.ListCompaniesAsync(tenantId, cancellationToken);
        var assignments = await repository.ListAssignmentsAsync(tenantId, cancellationToken);
        var postSuperiors = PostSuperiors.Of(
            assignments.Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId)),
            await repository.ListUnitPositionsAsync(tenantId, cancellationToken),
            await repository.ListUserPositionsAsync(tenantId, cancellationToken));
        var resolver = new HierarchyResolver(
            graph,
            companies.ToDictionary(company => company.Id, company => company.ManagerUserId),
            assignments.Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId,
                postSuperiors.TryGetValue(a.UserId, out var superior) ? superior : null)));
        var counting = (await repository.ListLinkTypesAsync(tenantId, cancellationToken))
            .Where(type => type.IsActive && type.CountsAsManager).Select(type => type.Id).ToHashSet();
        var links = (await repository.ListOrgLinksAsync(tenantId, cancellationToken))
            .Where(link => counting.Contains(link.LinkTypeId))
            .ToLookup(link => link.FromUnitId, link => link.ToUnitId);
        return (resolver, links);
    }

    private static LinkTypeDto ToDto(NodeState state, LinkType type) =>
        new(type.Id, type.Code, type.Name, type.LineStyle, type.CountsAsManager, type.IsActive, state.OrgLinks.Count(link => link.LinkTypeId == type.Id));

    private static OrgLinkDto ToDto(NodeState state, OrgLink link)
    {
        var type = state.LinkTypes[link.LinkTypeId];
        return new OrgLinkDto(link.Id, link.FromUnitId, state.Units.GetValueOrDefault(link.FromUnitId)?.Name ?? string.Empty,
            link.ToUnitId, state.Units.GetValueOrDefault(link.ToUnitId)?.Name ?? string.Empty, type.Id, type.Name, type.LineStyle, type.CountsAsManager);
    }
}
