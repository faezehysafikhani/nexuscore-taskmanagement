using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;

namespace Nexus.CompositionTests;

/// <summary>
/// Phase 6.1: a node's manager post (one per node), the post above set only on new posts, and the
/// posts each node type gets (applied to a node or a type; never changing or repeating a post).
/// </summary>
public sealed partial class OrgNodeApiTests
{
    [Fact]
    public async Task ANodeHasOneManagerPost_AndMarkingItChangesNoOtherPost()
    {
        var w = await World.BankAsync();
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var expert = await w.SeatAsync(it.Id, "کارشناس");
        var vp = await w.SeatAsync(it.Id, "معاون");
        var before = await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, it.Id, default));

        var marked = await w.Ok(w.Posts.SetManagerPostAsync(Tenant, vp, new SetManagerPostRequest(true), default));
        Assert.True(marked.IsManagerPost);
        Assert.Equal("conflict", (await w.Posts.SetManagerPostAsync(Tenant, expert, new SetManagerPostRequest(true), default)).Error.Code);
        // The expert's post above did not change (it was set before the mark).
        var after = await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, it.Id, default));
        Assert.Equal(before.Select(p => (p.Id, p.ParentPostId, p.Code)), after.Select(p => (p.Id, p.ParentPostId, p.Code)));

        // A new post with no post above given goes under the manager post; one given keeps it.
        var newPosition = await w.PositionAsync("کارشناس ارشد");
        var auto = await w.Ok(w.Service.AddUnitPositionAsync(Tenant, it.Id, new AddUnitPositionRequest(newPosition), default));
        Assert.Equal(vp, auto.ParentPostId);
        var given = await w.Ok(w.Service.AddUnitPositionAsync(Tenant, it.Id, new AddUnitPositionRequest(newPosition, ParentPostId: expert), default));
        Assert.Equal(expert, given.ParentPostId);

        // A node without a manager post: as before (no post above).
        var other = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت مالی", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        Assert.Null((await w.Ok(w.Service.AddUnitPositionAsync(Tenant, other.Id, new AddUnitPositionRequest(newPosition), default))).ParentPostId);

        // Unmarking: nothing else changes; a company's own post is never a node's manager post.
        Assert.False((await w.Ok(w.Posts.SetManagerPostAsync(Tenant, vp, new SetManagerPostRequest(false), default))).IsManagerPost);
        Assert.Equal(vp, (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, it.Id, default))).Single(p => p.Id == auto.Id).ParentPostId);
    }

    [Fact]
    public async Task ANodeTypesPosts_AreMadeOnce_AndExistingPostsStayAsTheyAre()
    {
        var w = await World.BankAsync();
        var (vpPosition, expertPosition) = (await w.PositionAsync("معاون"), await w.PositionAsync("کارشناس"));
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var finance = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت مالی", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var existing = await w.Ok(w.Service.AddUnitPositionAsync(Tenant, it.Id, new AddUnitPositionRequest(expertPosition), default));

        await w.Ok(w.Posts.SetNodeTypePostsAsync(Tenant, w.Type("deputy"), new SetNodeTypePostsRequest([
            new NodeTypePostRequest(vpPosition, IsManagerPost: true), new NodeTypePostRequest(expertPosition)]), default));
        Assert.Equal("validation.error", (await w.Posts.SetNodeTypePostsAsync(Tenant, w.Type("deputy"), new SetNodeTypePostsRequest([
            new NodeTypePostRequest(vpPosition, true), new NodeTypePostRequest(expertPosition, true)]), default)).Error.Code);
        Assert.Equal(2, (await w.Ok(w.Posts.GetNodeTypePostsAsync(Tenant, w.Type("deputy"), default))).Count);

        // One node: only what it lacks (the expert post is there); the existing post is not put under the new manager post.
        var one = await w.Ok(w.Posts.ApplyPostTemplatesAsync(Tenant, new ApplyPostTemplatesRequest(NodeId: it.Id), default));
        var made = Assert.Single(one.Created);
        Assert.True(made.IsManagerPost);
        Assert.Null((await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, it.Id, default))).Single(p => p.Id == existing.Id).ParentPostId);

        // The whole type: the other node gets both, the expert under its manager post; again: nothing.
        var all = await w.Ok(w.Posts.ApplyPostTemplatesAsync(Tenant, new ApplyPostTemplatesRequest(NodeTypeId: w.Type("deputy")), default));
        Assert.Equal(2, all.Created.Count);
        Assert.All(all.Created, p => Assert.Equal(finance.Id, p.UnitId));
        var managerPost = all.Created.Single(p => p.IsManagerPost);
        Assert.Equal(managerPost.Id, all.Created.Single(p => !p.IsManagerPost).ParentPostId);
        Assert.Empty((await w.Ok(w.Posts.ApplyPostTemplatesAsync(Tenant, new ApplyPostTemplatesRequest(NodeTypeId: w.Type("deputy")), default))).Created);
        // Codes stay unique in the company.
        var codes = (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Select(p => p.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());

        // A position that is not for the node's company is reported, not made.
        var otherCompany = await w.CompanyAsync("C2");
        var c2Node = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت شرکت دوم", CompanyId: otherCompany, NodeTypeId: w.Type("deputy")), default));
        var report = await w.Ok(w.Posts.ApplyPostTemplatesAsync(Tenant, new ApplyPostTemplatesRequest(NodeId: c2Node.Id), default));
        Assert.Empty(report.Created);
        Assert.Equal(2, report.Skipped.Count);
        Assert.Equal("validation.error", (await w.Posts.ApplyPostTemplatesAsync(Tenant, new ApplyPostTemplatesRequest(), default)).Error.Code);
    }

    [Fact]
    public async Task ACompanyFromATemplate_GetsItsPosts()
    {
        var w = new World();
        var simple = await w.Ok(w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("ساده", "S", null, OrgNodeService.SystemTemplateId("simple")), default));
        var simplePosts = (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Where(p => p.CompanyId == simple.Id).ToList();
        Assert.Equal(["مدیرعامل", "کارشناس"], simplePosts.OrderBy(p => p.SortOrder).Select(p => p.Title));
        Assert.All(simplePosts, p => Assert.Null(p.UnitId));

        var bank = await w.Ok(w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("بانک", "B", null, OrgNodeService.SystemTemplateId("bank")), default));
        var nodes = await w.Ok(w.Nodes.ListNodesAsync(Tenant, bank.Id, default));
        var posts = (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Where(p => p.CompanyId == bank.Id).ToList();
        var ceoOffice = nodes.Single(n => n.NodeTypeName == "مدیریت عامل");
        var ceoPosts = posts.Where(p => p.UnitId == ceoOffice.Id).ToList();
        Assert.Equal(["مدیرعامل", "قائم‌مقام مدیرعامل"], ceoPosts.OrderBy(p => p.SortOrder).Select(p => p.Title));
        Assert.Equal(ceoPosts.Single(p => p.IsManagerPost).Id, ceoPosts.Single(p => !p.IsManagerPost).ParentPostId);
        // The assembly, the board and committees get no posts.
        Assert.DoesNotContain(posts, p => nodes.Any(n => n.Id == p.UnitId && n.NodeTypeName is "کمیته" or "هیأت مدیره" or "مجمع عمومی"));
        // Every node has at most one manager post, codes are unique, and applying again makes nothing.
        Assert.All(posts.Where(p => p.IsManagerPost).GroupBy(p => p.UnitId), g => Assert.Single(g));
        Assert.Equal(posts.Count, posts.Select(p => p.Code).Distinct().Count());
        foreach (var type in nodes.Select(n => n.NodeTypeId).Distinct())
        {
            Assert.Empty((await w.Ok(w.Posts.ApplyPostTemplatesAsync(Tenant, new ApplyPostTemplatesRequest(NodeTypeId: type), default))).Created);
        }
    }
}
