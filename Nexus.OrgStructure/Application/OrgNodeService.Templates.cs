using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Nexus.Organization.Domain;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Structure templates (phase 4): the three built into the product (Templates/*.json) and the ones
/// an organization saved from its own companies. A company made from a template gets a copy: what
/// the template has is added to the organization (reusing what is already there by code or name),
/// and later changes to the template do not touch the company.
/// </summary>
public interface IOrgTemplateService
{
    Task<Result<IReadOnlyList<StructureTemplateDto>>> ListTemplatesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<StructureTemplateDetailDto>> GetTemplateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<Result<StructureTemplateDto>> SaveCompanyAsTemplateAsync(Guid tenantId, SaveStructureTemplateRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteTemplateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new (not yet saved) company with everything of the template, and saves it all.
    /// Posts are made by the caller (phase 6.1): this returns the template's posts directly in the
    /// company; the nodes' posts follow from the posts each node type gets.
    /// </summary>
    Task<Result<IReadOnlyList<CompanyPostToMake>>> CreateCompanyFromTemplateAsync(Guid tenantId, Company company, Guid templateId, CancellationToken cancellationToken);
}

public sealed partial class OrgNodeService : IOrgTemplateService
{
    private static readonly string[] SystemTemplateKeys = ["simple", "deputy", "bank"];

    private static readonly JsonSerializerOptions TemplateJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Lazy<IReadOnlyList<(Guid Id, TemplateModel Model, string Json)>> SystemTemplates = new(LoadSystemTemplates);

    /// <summary>A built-in template's id: the same everywhere and always (from its key).</summary>
    public static Guid SystemTemplateId(string key) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes("org-template:" + key)));

    // ---------------------------------------------------------------- reading

    public async Task<Result<IReadOnlyList<StructureTemplateDto>>> ListTemplatesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var list = SystemTemplates.Value.Select(t => ToDto(t.Id, t.Model, isSystem: true)).ToList();
        foreach (var saved in (await repository.ListStructureTemplatesAsync(tenantId, cancellationToken)).Where(t => !t.IsSystem).OrderBy(t => t.Name))
        {
            if (ReadModel(saved.Content) is { } model)
            {
                list.Add(ToDto(saved.Id, model with { Key = saved.Code, Name = saved.Name, Description = saved.Description }, isSystem: false));
            }
        }

        return Result.Success<IReadOnlyList<StructureTemplateDto>>(list);
    }

    public async Task<Result<StructureTemplateDetailDto>> GetTemplateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var found = await FindTemplateAsync(tenantId, id, cancellationToken);
        if (found is null)
        {
            return Result.Failure<StructureTemplateDetailDto>(TemplateNotFound());
        }

        using var document = JsonDocument.Parse(found.Value.Json);
        return Result.Success(new StructureTemplateDetailDto(ToDto(id, found.Value.Model, found.Value.IsSystem), document.RootElement.Clone()));
    }

    private async Task<(TemplateModel Model, string Json, bool IsSystem)?> FindTemplateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        if (SystemTemplates.Value.FirstOrDefault(t => t.Id == id) is { Model: not null } system)
        {
            return (system.Model, system.Json, true);
        }

        var saved = (await repository.ListStructureTemplatesAsync(tenantId, cancellationToken)).FirstOrDefault(t => t.Id == id && !t.IsSystem);
        return saved is not null && ReadModel(saved.Content) is { } model
            ? (model with { Key = saved.Code, Name = saved.Name, Description = saved.Description }, saved.Content!, false)
            : null;
    }

    // ---------------------------------------------------------------- saving and removing

    public async Task<Result<StructureTemplateDto>> SaveCompanyAsTemplateAsync(Guid tenantId, SaveStructureTemplateRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > MaxTypeNameLength)
        {
            return Result.Failure<StructureTemplateDto>(Error.Validation($"نام قالب را وارد کنید (حداکثر {MaxTypeNameLength} نویسه)."));
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is { Length: > 1000 })
        {
            return Result.Failure<StructureTemplateDto>(Error.Validation("توضیح قالب حداکثر ۱۰۰۰ نویسه است."));
        }

        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<StructureTemplateDto>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        if (!state.Companies.ContainsKey(request.CompanyId) || (mine is not null && !mine.Contains(request.CompanyId)))
        {
            return Result.Failure<StructureTemplateDto>(Error.NotFound("شرکت پیدا نشد."));
        }

        var saved = await repository.ListStructureTemplatesAsync(tenantId, cancellationToken);
        if (saved.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            || SystemTemplates.Value.Any(t => string.Equals(t.Model.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<StructureTemplateDto>(Error.Conflict($"قالبی با نام «{name}» وجود دارد."));
        }

        var code = request.Code?.Trim().ToLowerInvariant();
        bool CodeTaken(string c) => SystemTemplateKeys.Contains(c) || saved.Any(t => t.Code == c);
        if (string.IsNullOrEmpty(code))
        {
            var n = saved.Count + 1;
            while (CodeTaken($"custom-{n}"))
            {
                n++;
            }

            code = $"custom-{n}";
        }
        else if (!CodePattern().IsMatch(code) || code.Length > 40)
        {
            return Result.Failure<StructureTemplateDto>(Error.Validation("کد قالب فقط حروف کوچک لاتین، رقم و خط تیره است (حداکثر ۴۰)."));
        }
        else if (CodeTaken(code))
        {
            return Result.Failure<StructureTemplateDto>(Error.Conflict($"قالبی با کد «{code}» وجود دارد."));
        }

        var model = await ModelOfCompanyAsync(tenantId, state, request.CompanyId, code, name, description, cancellationToken);
        var template = new StructureTemplate(Guid.NewGuid(), tenantId, code, name, description, isSystem: false);
        template.SetContent(JsonSerializer.Serialize(model, TemplateJson));
        await repository.AddStructureTemplateAsync(template, cancellationToken);
        await LogAsync(tenantId, "StructureTemplate", template.Id, "save", new { code, name, company = request.CompanyId }, cancellationToken);
        await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken);
        return Result.Success(ToDto(template.Id, model, isSystem: false));
    }

    public async Task<Result> DeleteTemplateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        if (SystemTemplates.Value.Any(t => t.Id == id))
        {
            return Result.Failure(Error.Validation("قالب‌های آماده‌ی سامانه حذف نمی‌شوند."));
        }

        var template = (await repository.ListStructureTemplatesAsync(tenantId, cancellationToken)).FirstOrDefault(t => t.Id == id && !t.IsSystem);
        if (template is null)
        {
            return Result.Failure(TemplateNotFound());
        }

        // Companies made from it keep their copy; only the template goes.
        repository.RemoveStructureTemplate(template);
        await LogAsync(tenantId, "StructureTemplate", id, "delete", new { template.Code, template.Name }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>A company's structure in the template form: the types, rules, links and positions it uses, and its nodes.</summary>
    private async Task<TemplateModel> ModelOfCompanyAsync(Guid tenantId, NodeState state, Guid companyId, string code, string name, string? description, CancellationToken cancellationToken)
    {
        var company = state.Companies[companyId];
        var nodes = state.Units.Values.Where(unit => unit.IsActive && state.Graph.CompanyOfUnit(unit.Id) == companyId).ToDictionary(unit => unit.Id);

        var typeIds = nodes.Values.Where(unit => unit.NodeTypeId is not null).Select(unit => unit.NodeTypeId!.Value).ToHashSet();
        var types = state.NodeTypes.Values.Where(type => typeIds.Contains(type.Id)).OrderBy(type => type.SortOrder).ThenBy(type => type.Name).ToList();
        string TypeKey(Guid id) => state.NodeTypes[id].Code;

        var rules = state.Rules
            .Where(rule => typeIds.Contains(rule.ChildNodeTypeId) && (rule.ParentNodeTypeId is null || typeIds.Contains(rule.ParentNodeTypeId.Value)))
            .Select(rule => new TemplateRule(rule.ParentNodeTypeId is { } p ? TypeKey(p) : null, TypeKey(rule.ChildNodeTypeId)))
            .ToList();

        var links = state.OrgLinks.Where(link => nodes.ContainsKey(link.FromUnitId) && nodes.ContainsKey(link.ToUnitId)
                                                 && state.LinkTypes.ContainsKey(link.LinkTypeId)).ToList();
        var autoRules = state.AutoLinkRules.Where(rule => typeIds.Contains(rule.SourceNodeTypeId) && typeIds.Contains(rule.TargetNodeTypeId)
                                                          && state.LinkTypes.ContainsKey(rule.LinkTypeId)).ToList();
        var linkTypeIds = links.Select(link => link.LinkTypeId).Concat(autoRules.Select(rule => rule.LinkTypeId)).ToHashSet();
        var linkTypes = state.LinkTypes.Values.Where(type => linkTypeIds.Contains(type.Id)).OrderBy(type => type.Name).ToList();

        // Positions of the company, with the ones above them (so every parent resolves).
        var positions = (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToDictionary(position => position.Id);
        var used = (await repository.ListPositionCompaniesAsync(tenantId, cancellationToken))
            .Where(pc => pc.CompanyId == companyId && positions.ContainsKey(pc.PositionId)).Select(pc => pc.PositionId).ToHashSet();
        var postTemplates = (await repository.ListNodeTypePostTemplatesAsync(tenantId, cancellationToken))
            .Where(t => typeIds.Contains(t.NodeTypeId) && positions.ContainsKey(t.PositionId)).ToList();
        used.UnionWith(postTemplates.Select(t => t.PositionId));
        foreach (var id in used.ToList())
        {
            var seen = new HashSet<Guid> { id };
            for (var parent = positions[id].ParentPositionId; parent is { } p && positions.ContainsKey(p) && seen.Add(p); parent = positions[p].ParentPositionId)
            {
                used.Add(p);
            }
        }

        var positionKeys = new Dictionary<Guid, string>();
        foreach (var position in positions.Values.Where(p => used.Contains(p.Id)).OrderBy(p => p.Title))
        {
            positionKeys[position.Id] = $"p{positionKeys.Count + 1}";
        }

        var nodeKeys = new Dictionary<Guid, string>();
        List<TemplateNode> NodesUnder(Guid? parentId) => nodes.Values
            .Where(unit => parentId is { } p ? unit.ParentUnitId == p : state.Graph.ParentOf(unit.Id) is not { } up || !nodes.ContainsKey(up))
            .OrderBy(unit => unit.SortOrder).ThenBy(unit => unit.Name)
            .Select(unit =>
            {
                var key = nodeKeys[unit.Id] = $"n{nodeKeys.Count + 1}";
                var children = NodesUnder(unit.Id);
                return new TemplateNode(key, unit.Name, unit.NodeTypeId is { } t ? TypeKey(t) : PlainUnitCode, children.Count > 0 ? children : null);
            })
            .ToList();
        var tree = NodesUnder(null);

        // A node with no type is saved as the plain unit type: make sure that type is in the template.
        if (nodes.Values.Any(unit => unit.NodeTypeId is null) && types.All(type => type.Code != PlainUnitCode))
        {
            types.Add(state.NodeTypes.Values.FirstOrDefault(type => type.Code == PlainUnitCode) ?? new NodeType(Guid.Empty, tenantId, PlainUnitCode, "واحد", isDomainBoundary: true));
        }

        return new TemplateModel(
            1, code, name, description, company.UiLevel.ToString().ToLowerInvariant(),
            types.Select(type => new TemplateNodeType(type.Code, type.Name, type.Color, type.Icon, type.IsDomainBoundary)).ToList(),
            rules,
            linkTypes.Select(type => new TemplateLinkType(type.Code, type.Name, type.LineStyle, type.CountsAsManager)).ToList(),
            autoRules.Select(rule => new TemplateAutoLinkRule(TypeKey(rule.SourceNodeTypeId), TypeKey(rule.TargetNodeTypeId), state.LinkTypes[rule.LinkTypeId].Code)).ToList(),
            positionKeys.Select(pair => new TemplatePosition(pair.Value, positions[pair.Key].Title,
                positions[pair.Key].ParentPositionId is { } parent && positionKeys.TryGetValue(parent, out var parentKey) ? parentKey : null)).ToList(),
            postTemplates.GroupBy(t => t.NodeTypeId)
                .Select(group => new TemplatePostTemplate(TypeKey(group.Key), group.OrderBy(t => t.SortOrder)
                    .Select(t => new TemplatePost(positionKeys[t.PositionId], t.IsManagerPost ? true : null, null)).ToList()))
                .ToList(),
            [],
            tree,
            links.Select(link => new TemplateLink(nodeKeys[link.FromUnitId], nodeKeys[link.ToUnitId], state.LinkTypes[link.LinkTypeId].Code)).ToList());
    }

    // ---------------------------------------------------------------- making a company from a template

    public async Task<Result<IReadOnlyList<CompanyPostToMake>>> CreateCompanyFromTemplateAsync(Guid tenantId, Company company, Guid templateId, CancellationToken cancellationToken)
    {
        var found = await FindTemplateAsync(tenantId, templateId, cancellationToken);
        if (found is null)
        {
            return Result.Failure<IReadOnlyList<CompanyPostToMake>>(TemplateNotFound());
        }

        var model = found.Value.Model;
        if (TemplateProblem(model) is { } broken)
        {
            return Result.Failure<IReadOnlyList<CompanyPostToMake>>(Error.Validation($"قالب «{model.Name}» درست نیست: {broken}"));
        }

        if (!options.Value.AllowNesting && model.Nodes.Any(node => node.Children is { Count: > 0 }))
        {
            return Result.Failure<IReadOnlyList<CompanyPostToMake>>(Error.Validation(
                $"قالب «{model.Name}» گره‌های تودرتو دارد و قرار دادن گره زیر گره دیگر هنوز فعال نشده است (OrgStructure:AllowNesting)."));
        }

        await using var turn = await _lock.BeginAsync(tenantId, cancellationToken);
        if (turn is null)
        {
            return Result.Failure<IReadOnlyList<CompanyPostToMake>>(Busy());
        }

        var state = await LoadAsync(tenantId, cancellationToken);
        company.SetGroup(company.ParentCompanyId, templateId);
        if (Enum.TryParse<OrgUiLevel>(model.UiLevel, ignoreCase: true, out var level) && Enum.IsDefined(level))
        {
            company.SetUiLevel(level);
        }

        await repository.AddCompanyAsync(company, cancellationToken);

        // Node types: the one with the same code, or else the same name, or a new one.
        var types = new Dictionary<string, Guid>();
        var nextSort = state.NodeTypes.Count == 0 ? 0 : state.NodeTypes.Values.Max(type => type.SortOrder) + 1;
        var newTypes = 0;
        foreach (var item in model.NodeTypes)
        {
            var existing = state.NodeTypes.Values.FirstOrDefault(type => type.Code == item.Key)
                           ?? state.NodeTypes.Values.FirstOrDefault(type => Normalize(type.Name) == Normalize(item.Name));
            if (existing is not null)
            {
                types[item.Key] = existing.Id;
                continue;
            }

            var code = item.Key;
            for (var n = 2; state.NodeTypes.Values.Any(type => type.Code == code); n++)
            {
                code = $"{item.Key}-{n}";
            }

            var created = new NodeType(Guid.NewGuid(), tenantId, code, item.Name, item.IsDomainBoundary, nextSort++);
            created.SetStyle(item.Color ?? NodeType.DefaultColor, item.Icon ?? NodeType.DefaultIcon);
            await repository.AddNodeTypeAsync(created, cancellationToken);
            types[item.Key] = created.Id;
            newTypes++;
        }

        // Rules are for the whole organization: the template's are added to them. If it had none
        // (flat), the places its nodes are in now become rules first, so no node becomes a conflict.
        var ruleSet = state.Rules.Select(rule => (rule.ParentNodeTypeId, rule.ChildNodeTypeId)).ToHashSet();
        var wanted = new List<(Guid?, Guid)>();
        if (ruleSet.Count == 0 && model.Rules.Count > 0)
        {
            foreach (var unit in state.Units.Values.Where(unit => unit.IsActive && unit.NodeTypeId is not null))
            {
                var parent = state.Graph.ParentOf(unit.Id) is { } p ? state.Units[p] : null;
                if (parent is null || parent.NodeTypeId is not null)
                {
                    wanted.Add((parent?.NodeTypeId, unit.NodeTypeId!.Value));
                }
            }
        }

        wanted.AddRange(model.Rules.Select(rule => ((Guid?)(rule.Parent is { } parent ? types[parent] : null), types[rule.Child])));
        foreach (var (parent, child) in wanted.Where(rule => ruleSet.Add(rule)))
        {
            await repository.AddNodeTypeRuleAsync(new NodeTypeRule(Guid.NewGuid(), tenantId, parent, child), cancellationToken);
        }

        // Link types (by code, or else by name) and the automatic rules.
        var linkTypes = new Dictionary<string, LinkType>();
        foreach (var item in model.LinkTypes)
        {
            var existing = state.LinkTypes.Values.FirstOrDefault(type => type.Code == item.Key)
                           ?? state.LinkTypes.Values.FirstOrDefault(type => Normalize(type.Name) == Normalize(item.Name));
            if (existing is null)
            {
                var code = item.Key;
                for (var n = 2; state.LinkTypes.Values.Any(type => type.Code == code); n++)
                {
                    code = $"{item.Key}-{n}";
                }

                existing = new LinkType(Guid.NewGuid(), tenantId, code, item.Name, item.CountsAsManager && item.LineStyle != "dotted", item.LineStyle);
                await repository.AddLinkTypeAsync(existing, cancellationToken);
            }

            linkTypes[item.Key] = existing;
        }

        var autoRules = new List<(Guid Source, Guid Target, LinkType Type)>();
        foreach (var item in model.AutoLinkRules)
        {
            var rule = (types[item.SourceType], types[item.TargetType], linkTypes[item.LinkType]);
            autoRules.Add(rule);
            if (!state.AutoLinkRules.Any(r => r.SourceNodeTypeId == rule.Item1 && r.TargetNodeTypeId == rule.Item2))
            {
                await repository.AddAutoLinkRuleAsync(new AutoLinkRule(Guid.NewGuid(), tenantId, rule.Item1, rule.Item2, rule.Item3.Id), cancellationToken);
            }
        }

        // Positions: the one with the same title, or a new one; each is for this company too.
        var positions = (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToList();
        var positionCompanies = await repository.ListPositionCompaniesAsync(tenantId, cancellationToken);
        var positionIds = new Dictionary<string, Guid>();
        foreach (var item in OrderedPositions(model.Positions))
        {
            var existing = positions.FirstOrDefault(position => Normalize(position.Title) == Normalize(item.Title));
            if (existing is null)
            {
                existing = new Position(Guid.NewGuid(), tenantId, item.Title, item.Parent is { } parent ? positionIds[parent] : null);
                await repository.AddPositionAsync(existing, cancellationToken);
                positions.Add(existing);
            }

            positionIds[item.Key] = existing.Id;
            if (!positionCompanies.Any(pc => pc.PositionId == existing.Id && pc.CompanyId == company.Id))
            {
                await repository.AddPositionCompanyAsync(new PositionCompany(Guid.NewGuid(), tenantId, existing.Id, company.Id), cancellationToken);
            }
        }

        // The posts each node type should get (the caller then makes them).
        var existingPostTemplates = await repository.ListNodeTypePostTemplatesAsync(tenantId, cancellationToken);
        foreach (var item in model.PostTemplates)
        {
            var typeId = types[item.NodeType];
            if (existingPostTemplates.Any(t => t.NodeTypeId == typeId))
            {
                continue; // the organization already says what this type gets
            }

            var order = 0;
            foreach (var post in item.Posts)
            {
                await repository.AddNodeTypePostTemplateAsync(
                    new NodeTypePostTemplate(Guid.NewGuid(), tenantId, typeId, positionIds[post.Position], post.IsManagerPost == true, order++), cancellationToken);
            }
        }

        // Nodes: the top ones in the company, the rest under them.
        var nodeIds = new Dictionary<string, OrganizationUnit>();
        async Task AddNodesAsync(IReadOnlyList<TemplateNode> items, OrganizationUnit? parent)
        {
            var order = 0;
            foreach (var item in items)
            {
                var unit = new OrganizationUnit(Guid.NewGuid(), tenantId, item.Name);
                unit.SetParent(parent?.Id);
                unit.SetNodeType(types[item.Type]);
                unit.SetSortOrder(order++);
                await units.AddAsync(unit, cancellationToken);
                if (parent is null)
                {
                    await repository.AddCompanyUnitAsync(new CompanyUnit(Guid.NewGuid(), tenantId, company.Id, unit.Id), cancellationToken);
                }

                nodeIds[item.Key] = unit;
                await AddNodesAsync(item.Children ?? [], unit);
            }
        }

        await AddNodesAsync(model.Nodes, null);

        // Links: those the template names, then the automatic ones (one target in the company: linked;
        // none or several: not, as when a node is made).
        var linked = new HashSet<(Guid, Guid, Guid)>();
        foreach (var item in model.Links)
        {
            var link = (nodeIds[item.Source].Id, nodeIds[item.Target].Id, linkTypes[item.Type].Id);
            if (linked.Add(link))
            {
                await repository.AddOrgLinkAsync(new OrgLink(Guid.NewGuid(), tenantId, link.Item1, link.Item2, link.Item3), cancellationToken);
            }
        }

        foreach (var unit in nodeIds.Values)
        {
            foreach (var rule in autoRules.Where(rule => rule.Source == unit.NodeTypeId))
            {
                var targets = nodeIds.Values.Where(other => other.Id != unit.Id && other.NodeTypeId == rule.Target).ToList();
                if (targets.Count == 1 && linked.Add((unit.Id, targets[0].Id, rule.Type.Id)))
                {
                    await repository.AddOrgLinkAsync(new OrgLink(Guid.NewGuid(), tenantId, unit.Id, targets[0].Id, rule.Type.Id), cancellationToken);
                }
            }
        }

        await LogAsync(tenantId, "Company", company.Id, "create-from-template",
            new { template = templateId, key = model.Key, nodes = nodeIds.Count, newTypes, links = linked.Count }, cancellationToken);
        if (!await turn.SaveAsync(unitOfWork, unitsUnitOfWork, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<CompanyPostToMake>>(Busy());
        }

        // Its own posts (the head's...), for the caller to make together with its nodes' posts.
        return Result.Success<IReadOnlyList<CompanyPostToMake>>(model.CompanyPosts
            .Where(post => positionIds.ContainsKey(post.Position))
            .Select(post => new CompanyPostToMake(positionIds[post.Position], post.IsCompanyHead == true))
            .ToList());
    }

    /// <summary>Positions with each one's superior before it.</summary>
    private static IEnumerable<TemplatePosition> OrderedPositions(IReadOnlyList<TemplatePosition> positions)
    {
        var done = new HashSet<string>();
        var left = positions.ToList();
        while (left.Count > 0)
        {
            var ready = left.Where(p => p.Parent is null || done.Contains(p.Parent)).ToList();
            if (ready.Count == 0)
            {
                yield break; // a loop: caught by TemplateProblem first
            }

            foreach (var position in ready)
            {
                done.Add(position.Key);
                left.Remove(position);
                yield return position;
            }
        }
    }

    /// <summary>Why a template cannot be used (a reference that does not resolve), or null.</summary>
    private static string? TemplateProblem(TemplateModel model)
    {
        var types = model.NodeTypes.Select(t => t.Key).ToHashSet();
        var linkTypes = model.LinkTypes.Select(t => t.Key).ToHashSet();
        var positions = model.Positions.Select(p => p.Key).ToHashSet();
        if (types.Count != model.NodeTypes.Count || linkTypes.Count != model.LinkTypes.Count || positions.Count != model.Positions.Count)
        {
            return "کلید تکراری دارد.";
        }

        if (model.Rules.Any(r => !types.Contains(r.Child) || (r.Parent is { } p && !types.Contains(p)))
            || model.AutoLinkRules.Any(r => !types.Contains(r.SourceType) || !types.Contains(r.TargetType) || !linkTypes.Contains(r.LinkType))
            || model.PostTemplates.Any(t => !types.Contains(t.NodeType) || t.Posts.Any(post => !positions.Contains(post.Position)))
            || model.Positions.Any(p => p.Parent is { } parent && !positions.Contains(parent)))
        {
            return "به نوع، رابطه یا سمتی اشاره می‌کند که در آن نیست.";
        }

        if (OrderedPositions(model.Positions).Count() != model.Positions.Count)
        {
            return "سمت‌هایش حلقه دارند.";
        }

        var nodes = new HashSet<string>();
        bool NodesOk(IReadOnlyList<TemplateNode>? items) => (items ?? []).All(n => nodes.Add(n.Key) && types.Contains(n.Type) && NodesOk(n.Children));
        if (!NodesOk(model.Nodes))
        {
            return "گره‌هایش کلید تکراری یا نوع ناشناخته دارند.";
        }

        return model.Links.Any(l => !nodes.Contains(l.Source) || !nodes.Contains(l.Target) || !linkTypes.Contains(l.Type))
            ? "رابطه‌ای به گره یا نوع رابطه‌ای اشاره می‌کند که در آن نیست."
            : null;
    }

    // ---------------------------------------------------------------- the template form (Templates/structure-template.schema.json)

    private static IReadOnlyList<(Guid Id, TemplateModel Model, string Json)> LoadSystemTemplates()
    {
        var assembly = typeof(OrgStructureOptions).Assembly;
        var list = new List<(Guid, TemplateModel, string)>();
        foreach (var key in SystemTemplateKeys)
        {
            using var stream = assembly.GetManifestResourceStream($"Nexus.OrgStructure.Templates.{key}.json");
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            if (ReadModel(json) is { } model)
            {
                list.Add((SystemTemplateId(key), model, json));
            }
        }

        return list;
    }

    private static TemplateModel? ReadModel(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TemplateModel>(json, TemplateJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static StructureTemplateDto ToDto(Guid id, TemplateModel model, bool isSystem)
    {
        static int Count(IReadOnlyList<TemplateNode>? nodes) => (nodes ?? []).Sum(node => 1 + Count(node.Children));
        return new StructureTemplateDto(id, model.Key, model.Name, model.Description, isSystem, model.UiLevel, Count(model.Nodes), model.NodeTypes.Count);
    }

    private static Error TemplateNotFound() => Error.NotFound("قالب پیدا نشد.");

    private sealed record TemplateModel(
        int SchemaVersion,
        string Key,
        string Name,
        string? Description,
        string UiLevel,
        IReadOnlyList<TemplateNodeType> NodeTypes,
        IReadOnlyList<TemplateRule> Rules,
        IReadOnlyList<TemplateLinkType> LinkTypes,
        IReadOnlyList<TemplateAutoLinkRule> AutoLinkRules,
        IReadOnlyList<TemplatePosition> Positions,
        IReadOnlyList<TemplatePostTemplate> PostTemplates,
        IReadOnlyList<TemplateCompanyPost> CompanyPosts,
        IReadOnlyList<TemplateNode> Nodes,
        IReadOnlyList<TemplateLink> Links);

    private sealed record TemplateNodeType(string Key, string Name, string? Color, string? Icon, bool IsDomainBoundary);

    private sealed record TemplateRule(string? Parent, string Child);

    private sealed record TemplateLinkType(string Key, string Name, string LineStyle, bool CountsAsManager);

    private sealed record TemplateAutoLinkRule(string SourceType, string TargetType, string LinkType);

    private sealed record TemplatePosition(string Key, string Title, string? Parent);

    private sealed record TemplatePostTemplate(string NodeType, IReadOnlyList<TemplatePost> Posts);

    private sealed record TemplatePost(string Position, bool? IsManagerPost, bool? IsCompanyHead);

    private sealed record TemplateCompanyPost(string Position, bool? IsCompanyHead);

    private sealed record TemplateNode(string Key, string Name, string Type, IReadOnlyList<TemplateNode>? Children);

    private sealed record TemplateLink(string Source, string Target, string Type);
}
