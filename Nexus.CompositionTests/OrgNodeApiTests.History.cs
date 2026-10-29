using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;

namespace Nexus.CompositionTests;

/// <summary>
/// Phase 6.4: when a node exists (and the chart on a day), merging a node into another (open tasks
/// stay on their posts), the impact preview of a merge, the change log and undoing the last change.
/// </summary>
public sealed partial class OrgNodeApiTests
{
    [Fact]
    public async Task AMerge_MovesChildrenPostsAndPeople_TasksStayOnTheirPosts_AndUndoGivesTheSameAsBefore()
    {
        var w = await World.BankAsync();
        var history = (IOrgNodeHistoryService)w.Nodes;
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var digital = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت دیجیتال", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var software = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل نرم‌افزار", ParentNodeId: digital.Id, NodeTypeId: w.Type("gd")), default));
        var (itHead, digitalHead, expert, analyst) = (w.User("معاون فناوری"), w.User("معاون دیجیتال"), w.User("کارشناس"), w.User("تحلیلگر"));
        await w.Ok(w.Nodes.SetManagerAsync(Tenant, it.Id, new SetNodeManagerRequest(itHead), default));
        await w.Ok(w.Nodes.SetManagerAsync(Tenant, digital.Id, new SetNodeManagerRequest(digitalHead), default));
        foreach (var (user, node) in new[] { (itHead, it.Id), (digitalHead, digital.Id), (expert, software.Id), (analyst, digital.Id) })
        {
            await w.Ok(w.Service.SetAssignmentAsync(Tenant, user, new SetUserAssignmentRequest(w.Company, node, null, null), default));
        }

        // A post of the digital deputy, held, with an open task on it.
        var post = await w.SeatAsync(digital.Id, "کارشناس دیجیتال");
        await w.Repo.AddUserPositionAsync(new Nexus.OrgStructure.Domain.UserPosition(Guid.NewGuid(), Tenant, analyst, post), default);
        var task = Guid.NewGuid();
        await w.Repo.AddTaskPostAsync(new Nexus.OrgStructure.Domain.TaskPost(Guid.NewGuid(), Tenant, task, post, 0, analyst), default);
        await w.SaveAsync();

        async Task<string> Picture() => string.Join(";",
            (await w.Ok(w.Nodes.ListNodesAsync(Tenant, null, default))).OrderBy(n => n.Name).Select(n => $"{n.Name}>{n.ParentNodeId}:{n.IsActive}"),
            string.Join(",", (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).OrderBy(p => p.Id).Select(p => $"{p.Id}@{p.UnitId}:{p.IsManagerPost}")),
            string.Join(",", new[] { expert, analyst, digitalHead }.Select(u => w.Service.GetManagerAsync(Tenant, u, default).Result.Value?.UserId)));
        var before = await Picture();

        // The preview says what changes, and saves nothing.
        var preview = await w.Ok(w.Preview.PreviewAsync(Tenant, new ImpactPreviewRequest([new ImpactChangeRequest("merge", digital.Id, ToNodeId: it.Id)]), default));
        Assert.Equal((digitalHead, itHead), (preview.People.Single(p => p.Person.UserId == analyst).ManagerBefore!.UserId, preview.People.Single(p => p.Person.UserId == analyst).ManagerAfter!.UserId));
        Assert.Equal(before, await Picture());

        var merged = await w.Ok(history.MergeAsync(Tenant, digital.Id, new MergeNodeRequest(it.Id), default));
        Assert.False(merged.IsActive);
        Assert.Equal(it.Id, (await w.Ok(w.Nodes.GetNodeAsync(Tenant, software.Id, default))).ParentNodeId);
        Assert.Equal(it.Id, (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Single(p => p.Id == post).UnitId);
        Assert.Equal(itHead, (await w.Service.GetManagerAsync(Tenant, analyst, default)).Value!.UserId);
        // The open task is still on the same post, held by the same person.
        var onPost = Assert.Single(await w.Repo.ListTaskPostsAsync(Tenant, [task], null, default));
        Assert.Equal((post, analyst), (onPost.PostId, onPost.HolderUserId));
        // The merged node is gone from today's chart (ended), still there on yesterday's.
        Assert.DoesNotContain(await ChartNames(w, DateTimeOffset.UtcNow.AddMinutes(1)), name => name == "معاونت دیجیتال");
        Assert.Contains(await ChartNames(w, DateTimeOffset.UtcNow.AddDays(-1)), name => name == "معاونت دیجیتال");

        // Undo: the same picture as before the merge.
        Assert.True((await w.Ok(history.ListChangesAsync(Tenant, digital.Id, 10, default)))[0].CanUndo);
        await w.Ok(history.UndoLastChangeAsync(Tenant, digital.Id, new NodeVersionRequest(), default));
        Assert.Equal(before, await Picture());
        Assert.Equal("validation.error", (await history.UndoLastChangeAsync(Tenant, digital.Id, new NodeVersionRequest(), default)).Error.Code);

        // Not into itself, its own branch, or another company.
        Assert.Equal("validation.error", (await history.MergeAsync(Tenant, digital.Id, new MergeNodeRequest(software.Id), default)).Error.Code);
        var other = await w.CompanyAsync("C2");
        var elsewhere = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت شرکت دوم", CompanyId: other, NodeTypeId: w.Type("deputy")), default));
        Assert.Equal("validation.error", (await history.MergeAsync(Tenant, digital.Id, new MergeNodeRequest(elsewhere.Id), default)).Error.Code);
    }

    [Fact]
    public async Task AMergeIsNotUndone_OnceSomethingItMovedHasMovedAgain()
    {
        var w = await World.BankAsync();
        var history = (IOrgNodeHistoryService)w.Nodes;
        var a = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("الف", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var b = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("ب", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var c = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("ج", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var child = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل", ParentNodeId: b.Id, NodeTypeId: w.Type("gd")), default));
        await w.Ok(history.MergeAsync(Tenant, b.Id, new MergeNodeRequest(a.Id), default));
        await w.Ok(w.Nodes.MoveNodeAsync(Tenant, child.Id, new MoveNodeRequest(c.Id), default));
        Assert.Equal("conflict", (await history.UndoLastChangeAsync(Tenant, b.Id, new NodeVersionRequest(), default)).Error.Code);
    }

    [Fact]
    public async Task ANodesLastChange_IsUndone_AndDatesOnlyShapeTheChartOnADay()
    {
        var w = await World.BankAsync();
        var history = (IOrgNodeHistoryService)w.Nodes;
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var finance = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت مالی", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var gd = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل", ParentNodeId: it.Id, NodeTypeId: w.Type("gd")), default));

        await w.Ok(w.Nodes.UpdateNodeAsync(Tenant, gd.Id, new UpdateNodeRequest("اداره کل تازه"), default));
        Assert.Equal("اداره کل", (await w.Ok(history.UndoLastChangeAsync(Tenant, gd.Id, new NodeVersionRequest(), default))).Name);
        await w.Ok(w.Nodes.MoveNodeAsync(Tenant, gd.Id, new MoveNodeRequest(finance.Id), default));
        Assert.Equal(it.Id, (await w.Ok(history.UndoLastChangeAsync(Tenant, gd.Id, new NodeVersionRequest(), default))).ParentNodeId);
        await w.Ok(w.Nodes.SetDetailsAsync(Tenant, gd.Id, new SetNodeDetailsRequest("GD1", Phone: "1"), default));
        Assert.Null((await w.Ok(history.UndoLastChangeAsync(Tenant, gd.Id, new NodeVersionRequest(), default))).Code);
        var changes = await w.Ok(history.ListChangesAsync(Tenant, gd.Id, 50, default));
        Assert.Equal("details", changes[0].Action);
        Assert.Equal("validation.error", (await history.UndoLastChangeAsync(Tenant, (await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("تازه", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default))).Id, new NodeVersionRequest(), default)).Error.Code);

        // A node that starts next month: in today's chart only without a date (as always); not on today's date.
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("validation.error", (await history.SetValidityAsync(Tenant, finance.Id, new NodeValidityRequest(now, now.AddDays(-1)), default)).Error.Code);
        await w.Ok(history.SetValidityAsync(Tenant, finance.Id, new NodeValidityRequest(now.AddMonths(1)), default));
        Assert.Contains("معاونت مالی", await ChartNames(w, null));
        Assert.DoesNotContain("معاونت مالی", await ChartNames(w, now));
        Assert.Contains("معاونت مالی", await ChartNames(w, now.AddMonths(2)));
        // Nodes with no dates are in every day's chart, with their children.
        Assert.Contains("اداره کل", await ChartNames(w, now.AddYears(-10)));
    }

    private static async Task<List<string>> ChartNames(World w, DateTimeOffset? asOf)
    {
        var chart = await w.Ok(((OrgStructureService)w.Service).GetChartAsync(Tenant, null, asOf, default));
        var names = new List<string>();
        void Walk(IEnumerable<OrgChartUnitDto> units)
        {
            foreach (var unit in units)
            {
                names.Add(unit.Name);
                Walk(unit.Children);
            }
        }

        Walk(chart.SelectMany(company => company.Units));
        return names;
    }
}
