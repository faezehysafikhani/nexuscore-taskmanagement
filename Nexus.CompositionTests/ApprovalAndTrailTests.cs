using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Delegation.Application;
using Nexus.ProjectManagement.Delegation.Application.Dtos;
using Nexus.ProjectManagement.Delegation.Domain;
using Nexus.ProjectManagement.Delegation.Infrastructure;
using Nexus.ProjectManagement.History.Application;
using Nexus.ProjectManagement.History.Domain;
using Nexus.ProjectManagement.History.Infrastructure;
using Nexus.Workflow.Application;
using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using Nexus.Workflow.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>The three trails the screens read: what I decided and what I submitted (approval center), who held an item
/// (delegations) and what changed on an item (project history by entity).</summary>
public sealed class ApprovalAndTrailTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();

    // ------------------------------------------------------------ approval center: decided / requested

    private sealed class Center
    {
        public WorkflowDbContext Db { get; }
        public WorkflowInstanceService Service { get; }

        public Center()
        {
            Db = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Service = new WorkflowInstanceService(new WorkflowInstanceRepository(Db), Db, new WorkflowDefinitionRepository(Db));
        }

        /// <summary>A one-step approval started by <paramref name="requester"/> for a subject, waiting for <paramref name="approver"/>.</summary>
        public async Task<Guid> SubmitAsync(string subjectType, Guid requester, Guid approver)
        {
            var definition = new WorkflowDefinition(Guid.NewGuid(), Tenant, subjectType + " flow", subjectType);
            definition.AddStep(Guid.NewGuid(), "Final", approver, null);
            await Db.WorkflowDefinitions.AddAsync(definition);
            var instance = new WorkflowInstance(Guid.NewGuid(), Tenant, definition.Id, subjectType, Guid.NewGuid(),
                [new WorkflowInstanceStep(1, "Final", approver)])
            {
                CreatedByUserId = requester,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            await Db.WorkflowInstances.AddAsync(instance);
            await Db.SaveChangesAsync();
            return instance.Id;
        }
    }

    [Fact]
    public async Task Decided_ListsWhatTheUserDecided_ApprovedAndRejected_MostRecentFirst_AndNotWhatIsStillPending()
    {
        var c = new Center();
        var first = await c.SubmitAsync("Risk", Bob, Alice);
        var second = await c.SubmitAsync("Stakeholder", Bob, Alice);
        var pending = await c.SubmitAsync("Contract", Bob, Alice);
        var others = await c.SubmitAsync("Action", Bob, Carol);

        await c.Service.ApproveAsync(first, Alice, new("ok"), default);
        await Task.Delay(15);
        await c.Service.RejectAsync(second, Alice, new("no"), default);
        await c.Service.ApproveAsync(others, Carol, new(null), default);

        var decided = (await c.Service.ListDecidedByAsync(Tenant, Alice, 50, default)).Value!;

        Assert.Equal([second, first], decided.Select(i => i.Id));
        Assert.Equal(WorkflowInstanceStatus.Rejected, decided[0].Status);
        Assert.Equal("no", decided[0].Decisions.Single().Comment);
        Assert.DoesNotContain(decided, i => i.Id == pending);
        Assert.Equal([others], (await c.Service.ListDecidedByAsync(Tenant, Carol, 50, default)).Value!.Select(i => i.Id));
        Assert.Single((await c.Service.ListDecidedByAsync(Tenant, Alice, 1, default)).Value!);
        Assert.Empty((await c.Service.ListDecidedByAsync(Guid.NewGuid(), Alice, 50, default)).Value!);
    }

    [Fact]
    public async Task Requested_ListsWhatTheUserSubmitted_NewestFirst_WithStatusCurrentApproverAndWhen()
    {
        var c = new Center();
        var older = await c.SubmitAsync("Risk", Bob, Alice);
        await Task.Delay(15);
        var newer = await c.SubmitAsync("Stakeholder", Bob, Carol);
        await c.SubmitAsync("Contract", Alice, Carol);
        await c.Service.ApproveAsync(older, Alice, new(null), default);

        var requested = (await c.Service.ListRequestedByAsync(Tenant, Bob, 50, default)).Value!;

        Assert.Equal([newer, older], requested.Select(i => i.Id));
        Assert.Equal(Carol, requested[0].CurrentApproverUserId); // still pending: who it waits for
        Assert.Equal(WorkflowInstanceStatus.Approved, requested[1].Status);
        Assert.All(requested, i => { Assert.Equal(Bob, i.RequestedByUserId); Assert.NotNull(i.CreatedAtUtc); });
    }

    // ------------------------------------------------------------ delegation trail

    private static (ItemDelegationService Service, ItemDelegationDbContext Db) Delegations()
    {
        var db = new ItemDelegationDbContext(new DbContextOptionsBuilder<ItemDelegationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new ItemDelegationService(new ItemDelegationRepository(db), db), db);
    }

    [Fact]
    public async Task DelegationHistory_OfAnItem_KeepsEndedHandOvers_OldestFirst()
    {
        var (service, _) = Delegations();
        var item = Guid.NewGuid();
        var first = (await service.DelegateAsync(Tenant, Alice, new(DelegatedItemType.AgileTask, item, Project, Bob), default)).Value!;
        await Task.Delay(15);
        await service.EndAsync(Tenant, first.Id, Alice, false, default);
        await Task.Delay(15);
        await service.DelegateAsync(Tenant, Alice, new(DelegatedItemType.AgileTask, item, Project, Carol), default);
        await service.DelegateAsync(Tenant, Alice, new(DelegatedItemType.AgileTask, Guid.NewGuid(), Project, Bob), default);

        var trail = (await service.ListHistoryAsync(Tenant, Alice, false, null, DelegatedItemType.AgileTask, item, default)).Value!;

        Assert.Equal([Bob, Carol], trail.Select(d => d.DelegateUserId));
        Assert.Equal([false, true], trail.Select(d => d.IsActive));
        Assert.NotNull(trail[0].EndedAtUtc);
    }

    [Fact]
    public async Task DelegationHistory_OfAProject_IsFilteredToTheCallersOwnUnlessTheyMayViewAll()
    {
        var (service, _) = Delegations();
        await service.DelegateAsync(Tenant, Alice, new(DelegatedItemType.AgileTask, Guid.NewGuid(), Project, Bob), default);
        await service.DelegateAsync(Tenant, Carol, new(DelegatedItemType.Action, Guid.NewGuid(), Project, Bob), default);
        await service.DelegateAsync(Tenant, Carol, new(DelegatedItemType.Action, Guid.NewGuid(), Guid.NewGuid(), Alice), default); // another project

        Assert.Equal(2, (await service.ListHistoryAsync(Tenant, Bob, false, Project, null, null, default)).Value!.Count);   // received both
        Assert.Single((await service.ListHistoryAsync(Tenant, Alice, false, Project, null, null, default)).Value!);        // only her own
        Assert.Equal(2, (await service.ListHistoryAsync(Tenant, Guid.NewGuid(), true, Project, null, null, default)).Value!.Count);
        Assert.Empty((await service.ListHistoryAsync(Guid.NewGuid(), Bob, true, Project, null, null, default)).Value!);
    }

    [Fact]
    public async Task DelegationHistory_NeedsAProjectOrAnItem()
    {
        var (service, _) = Delegations();
        Assert.Equal("validation.error", (await service.ListHistoryAsync(Tenant, Alice, true, null, null, null, default)).Error.Code);
        Assert.Equal("validation.error", (await service.ListHistoryAsync(Tenant, Alice, true, null, null, Guid.NewGuid(), default)).Error.Code);
    }

    // ------------------------------------------------------------ project history by entity

    [Fact]
    public async Task ProjectHistory_CanBeNarrowedToOneEntity()
    {
        var db = new ProjectHistoryDbContext(new DbContextOptionsBuilder<ProjectHistoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new ProjectHistoryRepository(db);
        var task = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await repository.AddRangeAsync(
        [
            new ProjectChange(Guid.NewGuid(), Tenant, Project, "AgileTask", task, ProjectChangeKind.Modified, Alice, now, null),
            new ProjectChange(Guid.NewGuid(), Tenant, Project, "AgileTask", task, ProjectChangeKind.Added, Alice, now.AddMinutes(-5), null),
            new ProjectChange(Guid.NewGuid(), Tenant, Project, "AgileTask", Guid.NewGuid(), ProjectChangeKind.Modified, Alice, now, null),
        ], default);
        await db.SaveChangesAsync();

        var (items, total) = await repository.QueryAsync(new ProjectHistoryQuery(Tenant, Project, "AgileTask", EntityId: task), default);
        var (all, allTotal) = await repository.QueryAsync(new ProjectHistoryQuery(Tenant, Project), default);

        Assert.Equal(2, total);
        Assert.All(items, i => Assert.Equal(task, i.EntityId));
        Assert.Equal(3, allTotal);
    }
}
