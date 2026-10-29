using System.Text.Json;
using System.Text.RegularExpressions;
using Nexus.OrgStructure.Application;

namespace Nexus.CompositionTests;

/// <summary>
/// The ready-made structure templates (Nexus.OrgStructure/Templates, built into the assembly):
/// each one is complete and consistent in itself - every reference resolves, keys are unique,
/// every initial node is allowed under its parent, one company head, at most one manager post
/// per node type, a dotted line never counts as manager, and each automatic link finds exactly
/// one target. Loading them into a company comes later (phase 4); this only guards the data.
/// </summary>
public sealed class StructureTemplateFileTests
{
    private static readonly string[] TemplateKeys = ["simple", "deputy", "bank"];
    private static readonly Regex Key = new("^[a-z][a-z0-9]*(-[a-z0-9]+)*$");
    private static readonly Regex Color = new("^#[0-9a-fA-F]{6}$");

    private static JsonElement Load(string key)
    {
        var assembly = typeof(OrgStructureOptions).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Nexus.OrgStructure.Templates.{key}.json");
        Assert.True(stream is not null, $"Template '{key}' is not built into Nexus.OrgStructure.");
        using var document = JsonDocument.Parse(stream!);
        return document.RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Items(JsonElement template, string section) =>
        template.GetProperty(section).EnumerateArray();

    private static string Str(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    private static string? OptionalStr(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetString();

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.GetBoolean();

    private static HashSet<string> UniqueKeys(IEnumerable<JsonElement> items, string property, string what)
    {
        var keys = new HashSet<string>();
        foreach (var item in items)
        {
            var key = Str(item, property);
            Assert.Matches(Key, key);
            Assert.True(keys.Add(key), $"Duplicate {what} key '{key}'.");
        }

        return keys;
    }

    /// <summary>Every initial node with its parent's type (null: directly under the company).</summary>
    private static List<(string Key, string Name, string Type, string? ParentType, string? ParentKey)> Nodes(JsonElement template)
    {
        var result = new List<(string, string, string, string?, string?)>();
        void Walk(JsonElement nodes, string? parentType, string? parentKey)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                var key = Str(node, "key");
                var type = Str(node, "type");
                result.Add((key, Str(node, "name"), type, parentType, parentKey));
                if (node.TryGetProperty("children", out var children))
                {
                    Walk(children, type, key);
                }
            }
        }

        Walk(template.GetProperty("nodes"), null, null);
        return result;
    }

    [Fact]
    public void OnlyTheThreeTemplates_AreBuiltIn()
    {
        var resources = typeof(OrgStructureOptions).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Nexus.OrgStructure.Templates.", StringComparison.Ordinal))
            .Order()
            .ToArray();
        Assert.Equal(TemplateKeys.Select(key => $"Nexus.OrgStructure.Templates.{key}.json").Order().ToArray(), resources);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("deputy")]
    [InlineData("bank")]
    public void Template_IsCompleteAndConsistent(string templateKey)
    {
        var template = Load(templateKey);
        Assert.Equal(1, template.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(templateKey, Str(template, "key"));
        Assert.False(string.IsNullOrWhiteSpace(Str(template, "name")));
        Assert.Contains(Str(template, "uiLevel"), new[] { "simple", "normal", "advanced" });

        // Node types.
        var types = UniqueKeys(Items(template, "nodeTypes"), "key", "node type");
        foreach (var type in Items(template, "nodeTypes"))
        {
            Assert.False(string.IsNullOrWhiteSpace(Str(type, "name")));
            Assert.Matches(Color, Str(type, "color"));
            Assert.Matches(Key, Str(type, "icon"));
            Assert.True(type.GetProperty("isDomainBoundary").ValueKind is JsonValueKind.True or JsonValueKind.False);
        }

        // What may sit under what.
        var rules = new HashSet<(string?, string)>();
        foreach (var rule in Items(template, "rules"))
        {
            var parent = OptionalStr(rule, "parent");
            var child = Str(rule, "child");
            Assert.True(parent is null || types.Contains(parent), $"Rule parent '{parent}' is not a node type.");
            Assert.Contains(child, types);
            Assert.True(rules.Add((parent, child)), $"Duplicate rule {parent ?? "(company)"} → {child}.");
        }

        // Link types: a dotted line never counts as manager.
        var linkTypes = UniqueKeys(Items(template, "linkTypes"), "key", "link type");
        foreach (var linkType in Items(template, "linkTypes"))
        {
            Assert.Contains(Str(linkType, "lineStyle"), new[] { "solid", "dotted", "dashed" });
            if (Str(linkType, "lineStyle") == "dotted")
            {
                Assert.False(Flag(linkType, "countsAsManager"), $"Dotted link type '{Str(linkType, "key")}' counts as manager.");
            }
        }

        // Positions: references resolve and the superiors never loop.
        var positions = Items(template, "positions").ToDictionary(p => Str(p, "key"), p => OptionalStr(p, "parent"));
        UniqueKeys(Items(template, "positions"), "key", "position");
        foreach (var position in Items(template, "positions"))
        {
            var title = Str(position, "title");
            Assert.InRange(title.Length, 1, 120);
        }

        foreach (var (key, parent) in positions)
        {
            Assert.True(parent is null || positions.ContainsKey(parent), $"Position '{key}' has an unknown superior '{parent}'.");
            var seen = new HashSet<string> { key };
            for (var above = parent; above is not null; above = positions[above])
            {
                Assert.True(seen.Add(above), $"The superiors of position '{key}' loop.");
            }
        }

        // Post patterns: one per node type, known positions, at most one manager post.
        var postTypes = new HashSet<string>();
        var headTypes = new HashSet<string>();
        foreach (var pattern in Items(template, "postTemplates"))
        {
            var nodeType = Str(pattern, "nodeType");
            Assert.Contains(nodeType, types);
            Assert.True(postTypes.Add(nodeType), $"Two post patterns for node type '{nodeType}'.");
            var posts = pattern.GetProperty("posts").EnumerateArray().ToList();
            Assert.NotEmpty(posts);
            Assert.True(posts.Count(post => Flag(post, "isManagerPost")) <= 1, $"Node type '{nodeType}' has more than one manager post.");
            foreach (var post in posts)
            {
                Assert.Contains(Str(post, "position"), positions.Keys);
                if (Flag(post, "isCompanyHead"))
                {
                    // The head is the top of the chain: a position with no superior, and the node's manager post.
                    Assert.Null(positions[Str(post, "position")]);
                    Assert.True(Flag(post, "isManagerPost"), $"The company head of node type '{nodeType}' is not its manager post.");
                    headTypes.Add(nodeType);
                }
            }
        }

        var companyHeads = 0;
        foreach (var post in Items(template, "companyPosts"))
        {
            Assert.Contains(Str(post, "position"), positions.Keys);
            if (Flag(post, "isCompanyHead"))
            {
                Assert.Null(positions[Str(post, "position")]);
                companyHeads++;
            }
        }

        // Initial nodes: unique keys, known types, each allowed under its parent.
        var nodes = Nodes(template);
        var nodeKeys = new HashSet<string>();
        foreach (var node in nodes)
        {
            Assert.Matches(Key, node.Key);
            Assert.True(nodeKeys.Add(node.Key), $"Duplicate node key '{node.Key}'.");
            Assert.InRange(node.Name.Length, 1, 160);
            Assert.Contains(node.Type, types);
            Assert.True(rules.Contains((node.ParentType, node.Type)),
                $"Node '{node.Key}' ({node.Type}) is not allowed under {node.ParentType ?? "the company"}.");
        }

        // Exactly one company head once the template is applied.
        Assert.Equal(1, companyHeads + nodes.Count(node => headTypes.Contains(node.Type)));

        // Automatic links: known types, one rule per pair, and each finds exactly one target.
        var autoLinks = new HashSet<(string, string)>();
        foreach (var rule in Items(template, "autoLinkRules"))
        {
            var source = Str(rule, "sourceType");
            var target = Str(rule, "targetType");
            Assert.Contains(source, types);
            Assert.Contains(target, types);
            Assert.Contains(Str(rule, "linkType"), linkTypes);
            Assert.True(autoLinks.Add((source, target)), $"Two automatic link rules {source} → {target}.");
            if (nodes.Any(node => node.Type == source))
            {
                Assert.Single(nodes, node => node.Type == target);
            }
        }

        // Initial links: known nodes and types, never a node to itself, a duplicate, an automatic one or the main parent.
        var typeOf = nodes.ToDictionary(node => node.Key, node => node.Type);
        var parentOf = nodes.ToDictionary(node => node.Key, node => node.ParentKey);
        var links = new HashSet<(string, string, string)>();
        foreach (var link in Items(template, "links"))
        {
            var source = Str(link, "source");
            var target = Str(link, "target");
            var type = Str(link, "type");
            Assert.Contains(source, nodeKeys);
            Assert.Contains(target, nodeKeys);
            Assert.Contains(type, linkTypes);
            Assert.NotEqual(source, target);
            Assert.NotEqual(target, parentOf[source]);
            Assert.True(links.Add((source, target, type)), $"Duplicate link {source} → {target}.");
            Assert.DoesNotContain((typeOf[source], typeOf[target]), autoLinks);
        }
    }

    [Fact]
    public void Simple_IsJustTheHeadAndStaff_Flat()
    {
        var template = Load("simple");
        Assert.Empty(Items(template, "nodeTypes"));
        Assert.Empty(Items(template, "rules"));
        Assert.Empty(Nodes(template));
        Assert.Equal("simple", Str(template, "uiLevel"));
    }

    [Fact]
    public void Deputy_KeepsTheHeadDirectlyInTheCompany_AndUnitsInTheirDeputy()
    {
        var template = Load("deputy");
        Assert.Single(Items(template, "companyPosts"), post => Flag(post, "isCompanyHead"));
        var types = Items(template, "nodeTypes").ToDictionary(t => Str(t, "key"), t => Flag(t, "isDomainBoundary"));
        Assert.True(types["deputy"]);
        Assert.False(types["unit"]);
    }

    [Fact]
    public void Bank_FollowsThePostBankChart_AndTheCeoStaysTop()
    {
        var template = Load("bank");
        var nodes = Nodes(template);
        Assert.Equal(52, nodes.Count);

        // مجمع عمومی → هیأت مدیره → مدیریت عامل; the committees beside, linked to the board automatically.
        Assert.Equal(("general-assembly", (string?)null), nodes.Where(n => n.Key == "general-assembly").Select(n => (n.Type, n.ParentKey)).Single());
        Assert.Equal("general-assembly", nodes.Single(n => n.Key == "board").ParentKey);
        Assert.Equal("board", nodes.Single(n => n.Key == "ceo-office").ParentKey);
        var committees = nodes.Where(n => n.Type == "committee").ToList();
        Assert.Equal(5, committees.Count);
        Assert.All(committees, committee => Assert.Null(committee.ParentKey));
        Assert.Contains(Items(template, "autoLinkRules"), rule => Str(rule, "sourceType") == "committee" && Str(rule, "targetType") == "board");

        // Under the CEO: 8 staff units, 5 deputies and 4 affairs managements (with their 25 general
        // departments and the digital branch), and the provincial branch management.
        Assert.Equal(8, nodes.Count(n => n.Type == "staff-unit" && n.ParentKey == "ceo-office"));
        Assert.Equal(5, nodes.Count(n => n.Type == "deputy" && n.ParentKey == "ceo-office"));
        Assert.Equal(4, nodes.Count(n => n.Type == "affairs-management" && n.ParentKey == "ceo-office"));
        Assert.Equal(25, nodes.Count(n => n.Type == "general-department"));
        Assert.Single(nodes, n => n.Type == "branch-management" && n.ParentKey == "ceo-office");

        // The governance bodies have no posts (so no manager): nobody becomes the CEO's manager.
        var withPosts = Items(template, "postTemplates").Select(p => Str(p, "nodeType")).ToHashSet();
        Assert.DoesNotContain("general-assembly", withPosts);
        Assert.DoesNotContain("board", withPosts);
        Assert.DoesNotContain("committee", withPosts);
        Assert.Empty(Items(template, "companyPosts"));
        var ceoOffice = Items(template, "postTemplates").Single(p => Str(p, "nodeType") == "ceo-office");
        Assert.Contains(ceoOffice.GetProperty("posts").EnumerateArray(), post => Str(post, "position") == "ceo" && Flag(post, "isCompanyHead"));

        // A general department under a deputy belongs to that deputy's domain; staff units have their own.
        var boundary = Items(template, "nodeTypes").ToDictionary(t => Str(t, "key"), t => Flag(t, "isDomainBoundary"));
        Assert.False(boundary["general-department"]);
        Assert.True(boundary["deputy"]);
        Assert.True(boundary["affairs-management"]);
        Assert.True(boundary["staff-unit"]);
    }
}
