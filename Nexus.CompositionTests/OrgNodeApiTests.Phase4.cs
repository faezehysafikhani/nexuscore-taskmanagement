using System.Text.Json;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Permissions;

namespace Nexus.CompositionTests;

/// <summary>
/// Phase 4 of the dynamic structure: structure templates (the three built in, saving a company as
/// one, making a company from one), secondary links (their types, the automatic rules with a
/// preview, links that count as manager) and links in the impact preview.
/// </summary>
public sealed partial class OrgNodeApiTests
{
    private static async Task Done(Task<NexusCore.SharedKernel.Results.Result> call)
    {
        var result = await call;
        Assert.True(result.IsSuccess, result.IsSuccess ? null : $"{result.Error.Code}: {result.Error.Message}");
    }

    // ------------------------------------------------------------------ templates

    private static int CountNodes(JsonElement nodes) =>
        nodes.EnumerateArray().Sum(node => 1 + (node.TryGetProperty("children", out var children) ? CountNodes(children) : 0));

    [Fact]
    public async Task TheThreeBuiltInTemplates_EachMakeACompany()
    {
        var w = new World();
        var templates = await w.Ok(w.Templates.ListTemplatesAsync(Tenant, default));
        Assert.Equal(["simple", "deputy", "bank"], templates.Select(t => t.Code));
        Assert.All(templates, t => Assert.True(t.IsSystem));
        Assert.Equal(OrgNodeService.SystemTemplateId("bank"), templates.Single(t => t.Code == "bank").Id);

        foreach (var template in templates)
        {
            var company = await w.Ok(w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("شرکت " + template.Name, "T-" + template.Code, null, template.Id), default));
            var chart = Assert.Single((await w.Service.GetChartAsync(Tenant, company.Id, default)).Value!);
            Assert.Equal(template.UiLevel, chart.UiLevel);

            var detail = await w.Ok(w.Templates.GetTemplateAsync(Tenant, template.Id, default));
            var nodes = await w.Ok(w.Nodes.ListNodesAsync(Tenant, company.Id, default));
            Assert.Equal(CountNodes(detail.Content.GetProperty("nodes")), nodes.Count);
            Assert.Equal(template.NodeCount, nodes.Count);

            // Its positions are for the new company.
            var positions = (await w.Service.ListPositionsAsync(Tenant, default)).Value!;
            foreach (var position in detail.Content.GetProperty("positions").EnumerateArray())
            {
                Assert.Contains(positions, p => p.Title == position.GetProperty("title").GetString() && p.CompanyIds.Contains(company.Id));
            }
        }

        // The bank: nested nodes, every committee linked to the board (automatic rule), the other links of the template.
        var bank = (await w.Ok(w.Service.ListCompaniesAsync(Tenant, default))).Single(c => c.Code == "T-bank").Id;
        var bankNodes = await w.Ok(w.Nodes.ListNodesAsync(Tenant, bank, default));
        var committees = bankNodes.Where(n => n.NodeTypeName == "کمیته").ToList();
        var board = bankNodes.Single(n => n.NodeTypeName == "هیأت مدیره");
        var links = await w.Ok(w.Links.ListLinksAsync(Tenant, null, default));
        Assert.NotEmpty(committees);
        Assert.All(committees, c => Assert.Contains(links, l => l.FromNodeId == c.Id && l.ToNodeId == board.Id && l.LinkTypeName == "کمیته‌ی هیأت مدیره"));
        var bankDetail = await w.Ok(w.Templates.GetTemplateAsync(Tenant, OrgNodeService.SystemTemplateId("bank"), default));
        Assert.Equal(committees.Count + bankDetail.Content.GetProperty("links").GetArrayLength(), links.Count);
        Assert.All(links, l => Assert.False(l.CountsAsManager));
        Assert.Contains(bankNodes, n => n.ParentNodeId is not null);
        // Its nodes follow the rules it brought.
        Assert.Empty((await w.Ok(w.Nodes.GetRulesAsync(Tenant, default))).Conflicts);
    }

    [Fact]
    public async Task ANestedTemplate_IsRefusedWithNestingOff_AndAFlatOneIsNot()
    {
        var w = new World(allowNesting: false);
        var refused = await w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("بانک", "B", null, OrgNodeService.SystemTemplateId("bank")), default);
        Assert.Equal("validation.error", refused.Error.Code);
        Assert.Contains("AllowNesting", refused.Error.Message);
        Assert.Empty((await w.Ok(w.Service.ListCompaniesAsync(Tenant, default))));

        await w.Ok(w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("ساده", "S", null, OrgNodeService.SystemTemplateId("simple")), default));
        Assert.Equal("not_found", (await w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("x", "X", null, Guid.NewGuid()), default)).Error.Code);
    }

    [Fact]
    public async Task ACompanyMadeFromATemplate_IsACopy_TheTemplateChangingOrGoingDoesNotTouchIt()
    {
        var w = new World();
        var first = await w.Ok(w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("بانک یک", "B1", null, OrgNodeService.SystemTemplateId("bank")), default));
        var firstNodes = await w.Ok(w.Nodes.ListNodesAsync(Tenant, first.Id, default));

        // Saved as the organization's own template, then one more node in the company.
        var saved = await w.Ok(w.Templates.SaveCompanyAsTemplateAsync(Tenant, new SaveStructureTemplateRequest(first.Id, "بانک ما"), default));
        Assert.False(saved.IsSystem);
        Assert.Equal(firstNodes.Count, saved.NodeCount);
        Assert.Equal("conflict", (await w.Templates.SaveCompanyAsTemplateAsync(Tenant, new SaveStructureTemplateRequest(first.Id, "بانک ما"), default)).Error.Code);
        await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("کمیته تازه", CompanyId: first.Id, NodeTypeId: firstNodes.Single(n => n.NodeTypeName == "کمیته" && n.ParentNodeId is null && n.Name.Contains("ریسک")).NodeTypeId), default));

        // A company from the saved template has the structure as it was saved.
        var second = await w.Ok(w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("بانک دو", "B2", null, saved.Id), default));
        var secondNodes = await w.Ok(w.Nodes.ListNodesAsync(Tenant, second.Id, default));
        Assert.Equal(firstNodes.Count, secondNodes.Count);
        Assert.Equal(firstNodes.Select(n => (n.Name, n.NodeTypeId)).Order(), secondNodes.Select(n => (n.Name, n.NodeTypeId)).Order());
        // Types are reused, not made again.
        Assert.Equal(13, (await w.Ok(w.Nodes.ListNodeTypesAsync(Tenant, default))).Count); // company, unit + the bank's 11

        // The template goes: both companies stay as they are. A built-in one cannot go.
        await Done(w.Templates.DeleteTemplateAsync(Tenant, saved.Id, default));
        Assert.DoesNotContain(await w.Ok(w.Templates.ListTemplatesAsync(Tenant, default)), t => t.Id == saved.Id);
        Assert.Equal(firstNodes.Count + 1, (await w.Ok(w.Nodes.ListNodesAsync(Tenant, first.Id, default))).Count);
        Assert.Equal(secondNodes.Count, (await w.Ok(w.Nodes.ListNodesAsync(Tenant, second.Id, default))).Count);
        Assert.Equal("validation.error", (await w.Templates.DeleteTemplateAsync(Tenant, OrgNodeService.SystemTemplateId("bank"), default)).Error.Code);
    }

    [Fact]
    public async Task ATemplateOnAFlatOrganization_LeavesItsNodesWhereTheyAre_WithoutConflicts()
    {
        var w = new World();
        var old = await w.CompanyAsync("OLD");
        await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت قدیمی", CompanyId: old), default));
        Assert.Empty((await w.Ok(w.Nodes.GetRulesAsync(Tenant, default))).Rules);

        await w.Ok(w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("معاونتی", "D", null, OrgNodeService.SystemTemplateId("deputy")), default));
        var rules = await w.Ok(w.Nodes.GetRulesAsync(Tenant, default));
        Assert.Empty(rules.Conflicts);
        // The plain unit stays allowed at the top (it was there), and under a deputy (the template's rule).
        Assert.Contains(new NodeTypeRuleDto(null, w.PlainType), rules.Rules);
        Assert.Contains(rules.Rules, r => r.ChildNodeTypeId == w.PlainType && r.ParentNodeTypeId is not null);
        // Another node at the top of the old company is still fine.
        await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت دوم", CompanyId: old), default));
    }

    // ------------------------------------------------------------------ links

    [Fact]
    public async Task ADottedLink_NeverCountsAsManager()
    {
        var w = new World();
        Assert.Equal("validation.error", (await w.Links.CreateLinkTypeAsync(Tenant, new CreateLinkTypeRequest("نظارت", LineStyle: "dotted", CountsAsManager: true), default)).Error.Code);
        var solid = await w.Ok(w.Links.CreateLinkTypeAsync(Tenant, new CreateLinkTypeRequest("گزارش", LineStyle: "solid", CountsAsManager: true), default));
        Assert.Equal("validation.error", (await w.Links.UpdateLinkTypeAsync(Tenant, solid.Id, new UpdateLinkTypeRequest("گزارش", "dotted", true, true), default)).Error.Code);
        Assert.Equal("validation.error", (await w.Links.CreateLinkTypeAsync(Tenant, new CreateLinkTypeRequest("x", LineStyle: "wavy"), default)).Error.Code);
    }

    [Fact]
    public async Task ACommittee_IsLinkedToTheBoard_AndSeveralBoardsAreLeftToTheUser()
    {
        var w = await World.BankAsync();
        var board = (await w.Ok(w.Nodes.CreateNodeTypeAsync(Tenant, new CreateNodeTypeRequest("هیأت مدیره", "board"), default))).Id;
        var rules = (await w.Ok(w.Nodes.GetRulesAsync(Tenant, default))).Rules.Append(new NodeTypeRuleDto(null, board)).ToList();
        await w.Ok(w.Nodes.SetRulesAsync(Tenant, new SetNodeTypeRulesRequest(rules), default));
        var linkType = await w.Ok(w.Links.CreateLinkTypeAsync(Tenant, new CreateLinkTypeRequest("کمیته‌ی هیأت مدیره"), default));
        Assert.Equal("dotted", linkType.LineStyle);
        await w.Ok(w.Links.SetAutoLinkRulesAsync(Tenant, new SetAutoLinkRulesRequest([new AutoLinkRuleDto(w.Type("committee"), board, linkType.Id)]), default));

        // No board yet: nothing is linked (the rule waits); one board: linked at once.
        var early = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("کمیته زودهنگام", CompanyId: w.Company, NodeTypeId: w.Type("committee")), default));
        Assert.Null(early.AutoLinkChoices);
        Assert.Empty(await w.Ok(w.Links.ListLinksAsync(Tenant, early.Id, default)));
        var theBoard = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("هیأت مدیره", CompanyId: w.Company, NodeTypeId: board), default));
        var audit = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("کمیته حسابرسی", CompanyId: w.Company, NodeTypeId: w.Type("committee")), default));
        var link = Assert.Single(await w.Ok(w.Links.ListLinksAsync(Tenant, audit.Id, default)));
        Assert.Equal(theBoard.Id, link.ToNodeId);

        // The earlier committee: in the plan; applied only as planned.
        var plan = await w.Ok(w.Links.PlanAutoLinksAsync(Tenant, default));
        var proposed = Assert.Single(plan.Links);
        Assert.Equal((early.Id, theBoard.Id), (proposed.FromNodeId, proposed.ToNodeId));
        Assert.Equal("conflict", (await w.Links.ApplyAutoLinksAsync(Tenant, new ApplyAutoLinksRequest([new CreateLinkRequest(audit.Id, early.Id, linkType.Id)]), default)).Error.Code);
        await w.Ok(w.Links.ApplyAutoLinksAsync(Tenant, new ApplyAutoLinksRequest([new CreateLinkRequest(proposed.FromNodeId, proposed.ToNodeId, proposed.LinkTypeId)]), default));
        Assert.Empty((await w.Ok(w.Links.PlanAutoLinksAsync(Tenant, default))).Links);

        // Two boards: the next committee gets the choice, and no link.
        var second = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("هیأت مدیره دوم", CompanyId: w.Company, NodeTypeId: board), default));
        var risk = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("کمیته ریسک", CompanyId: w.Company, NodeTypeId: w.Type("committee")), default));
        var choice = Assert.Single(risk.AutoLinkChoices!);
        Assert.Equal(new[] { theBoard.Id, second.Id }.Order(), choice.Targets.Select(t => t.Id).Order());
        Assert.Empty(await w.Ok(w.Links.ListLinksAsync(Tenant, risk.Id, default)));
        Assert.Single((await w.Ok(w.Links.PlanAutoLinksAsync(Tenant, default))).Ambiguous, a => a.NodeId == risk.Id);

        // The user picks one; the chart shows it.
        await w.Ok(w.Links.CreateLinkAsync(Tenant, new CreateLinkRequest(risk.Id, second.Id, linkType.Id), default));
        Assert.Equal("conflict", (await w.Links.CreateLinkAsync(Tenant, new CreateLinkRequest(risk.Id, second.Id, linkType.Id), default)).Error.Code);
        Assert.Equal("validation.error", (await w.Links.CreateLinkAsync(Tenant, new CreateLinkRequest(risk.Id, risk.Id, linkType.Id), default)).Error.Code);
        var chartRisk = Assert.Single((await w.Service.GetChartAsync(Tenant, null, default)).Value!).Units.Single(u => u.UnitId == risk.Id);
        Assert.Equal(second.Id, Assert.Single(chartRisk.Links!).TargetNodeId);
    }

    [Fact]
    public async Task ALinkThatCountsAsManager_GivesAnExtraManager_AndNoneChangesNothingElse()
    {
        var w = await World.BankAsync();
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var risk = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("کمیته ریسک", CompanyId: w.Company, NodeTypeId: w.Type("committee")), default));
        var security = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل امنیت", ParentNodeId: it.Id, NodeTypeId: w.Type("gd")), default));
        var (deputyHead, riskHead, expert) = (w.User("معاون فناوری"), w.User("رئیس کمیته ریسک"), w.User("کارشناس امنیت"));
        await w.Ok(w.Nodes.SetManagerAsync(Tenant, it.Id, new SetNodeManagerRequest(deputyHead), default));
        await w.Ok(w.Nodes.SetManagerAsync(Tenant, risk.Id, new SetNodeManagerRequest(riskHead), default));
        foreach (var (user, node) in new[] { (deputyHead, it.Id), (riskHead, risk.Id), (expert, security.Id) })
        {
            await w.Ok(w.Service.SetAssignmentAsync(Tenant, user, new SetUserAssignmentRequest(w.Company, node, null, null), default));
        }

        async Task<(Guid?, string)> Hierarchy() =>
            ((await w.Service.GetManagerAsync(Tenant, expert, default)).Value?.UserId,
             string.Join(",", (await w.Service.GetSubordinatesAsync(Tenant, deputyHead, true, default)).Value!.Select(s => s.UserId).Order()));
        var before = await Hierarchy();

        // A dotted link (does not count): nothing changes, no extra manager.
        var dotted = await w.Ok(w.Links.CreateLinkTypeAsync(Tenant, new CreateLinkTypeRequest("نظارت"), default));
        await w.Ok(w.Links.CreateLinkAsync(Tenant, new CreateLinkRequest(security.Id, risk.Id, dotted.Id), default));
        Assert.Equal(before, await Hierarchy());
        Assert.Empty(await w.Ok(w.Links.GetExtraManagersAsync(Tenant, expert, default)));

        // A solid link that counts: the preview shows the extra manager first; nothing is saved by it.
        var counting = await w.Ok(w.Links.CreateLinkTypeAsync(Tenant, new CreateLinkTypeRequest("گزارش‌دهی", LineStyle: "solid", CountsAsManager: true), default));
        var preview = await w.Ok(w.Preview.PreviewAsync(Tenant, new ImpactPreviewRequest([new ImpactChangeRequest("link", security.Id, ToNodeId: risk.Id, LinkTypeId: counting.Id)]), default));
        var forExpert = preview.People.Single(p => p.Person.UserId == expert);
        Assert.Equal(riskHead, Assert.Single(forExpert.ExtraManagersAdded!).UserId);
        Assert.False(forExpert.ManagerChanged);
        Assert.Empty(await w.Ok(w.Links.GetExtraManagersAsync(Tenant, expert, default)));

        var made = await w.Ok(w.Links.CreateLinkAsync(Tenant, new CreateLinkRequest(security.Id, risk.Id, counting.Id), default));
        Assert.Equal(riskHead, Assert.Single(await w.Ok(w.Links.GetExtraManagersAsync(Tenant, expert, default))).UserId);
        // The main manager and everything else stay as they were.
        Assert.Equal(before, await Hierarchy());

        // Removing it, previewed: the extra manager goes.
        var removal = await w.Ok(w.Preview.PreviewAsync(Tenant, new ImpactPreviewRequest([new ImpactChangeRequest("unlink-link", LinkId: made.Id)]), default));
        Assert.Equal(riskHead, Assert.Single(removal.People.Single(p => p.Person.UserId == expert).ExtraManagersRemoved!).UserId);
        await Done(w.Links.DeleteLinkAsync(Tenant, made.Id, default));
        Assert.Empty(await w.Ok(w.Links.GetExtraManagersAsync(Tenant, expert, default)));
    }

    [Fact]
    public async Task TheUiLevel_IsSetPerCompany()
    {
        var w = await World.BankAsync();
        Assert.Equal("validation.error", (await w.Links.SetCompanyUiLevelAsync(Tenant, w.Company, new SetUiLevelRequest("expert"), default)).Error.Code);
        Assert.True((await w.Links.SetCompanyUiLevelAsync(Tenant, w.Company, new SetUiLevelRequest("advanced"), default)).IsSuccess);
        Assert.Equal("advanced", Assert.Single((await w.Service.GetChartAsync(Tenant, null, default)).Value!).UiLevel);
    }

    [Fact]
    public void ManagingTemplates_IsItsOwnPermission()
    {
        Assert.Contains(OrgStructurePermissions.All, p => p.Name == OrgStructurePermissions.ManageTemplates);
        Assert.Equal([OrgStructurePermissions.ManageTemplates], OrgStructurePermissions.NodePolicies[OrgStructurePermissions.ManageTemplates]);
    }
}
