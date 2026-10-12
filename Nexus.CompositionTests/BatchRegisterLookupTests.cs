using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Documents.Application;
using Nexus.ProjectManagement.Documents.Application.Dtos;
using Nexus.ProjectManagement.Documents.Domain;
using Nexus.ProjectManagement.Documents.Infrastructure;
using Nexus.ProjectManagement.Progress.Application;
using Nexus.ProjectManagement.Progress.Application.Dtos;
using Nexus.ProjectManagement.Progress.Domain;
using Nexus.ProjectManagement.Progress.Infrastructure;
using Nexus.Workflow.Application;
using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using Nexus.Workflow.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>The "several projects in one call" lookups the screens that show every project's items need.</summary>
public sealed class BatchRegisterLookupTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid P1 = Guid.NewGuid();
    private static readonly Guid P2 = Guid.NewGuid();
    private static readonly Guid P3 = Guid.NewGuid();

    private sealed class NoApprovals : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    [Fact]
    public async Task ProgressUpdates_AreListedForSeveralProjectsAtOnce_AndOnlyForThose()
    {
        var db = new ProgressDbContext(new DbContextOptionsBuilder<ProgressDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new ProgressService(new ProgressRepository(db), db, new NoApprovals());
        var day = new DateOnly(2026, 10, 12);
        foreach (var project in new[] { P1, P2, P3 })
        {
            Assert.True((await service.CreateAsync(new CreateProgressUpdateRequest(Tenant, project, "status", day, 10, 8, null), default)).IsSuccess);
        }

        var result = await service.ListForProjectsAsync(new ListProgressUpdatesForProjectsRequest([P1, P3, P3]), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { P1, P3 }.OrderBy(x => x), result.Value!.Select(u => u.ProjectId).OrderBy(x => x));
        Assert.Empty((await service.ListForProjectsAsync(new ListProgressUpdatesForProjectsRequest([]), default)).Value!);
    }

    [Fact]
    public async Task DelayReasons_AreListedForSeveralProjectsAtOnce()
    {
        var db = new ProgressDbContext(new DbContextOptionsBuilder<ProgressDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new DelayReasonService(new DelayReasonRepository(db), db, new NoApprovals());
        var day = new DateOnly(2026, 10, 12);
        foreach (var project in new[] { P1, P2, P3 })
        {
            Assert.True((await service.CreateAsync(new CreateDelayReasonRequest(Tenant, project, day, DelayRootCause.Other, "late", null, null, null), default)).IsSuccess);
        }

        var result = await service.ListForProjectsAsync(new ListDelayReasonsForProjectsRequest([P2, P3]), default);

        Assert.Equal(new[] { P2, P3 }.OrderBy(x => x), result.Value!.Select(r => r.ProjectId).OrderBy(x => x));
        Assert.Empty((await service.ListForProjectsAsync(new ListDelayReasonsForProjectsRequest([]), default)).Value!);
    }

    [Fact]
    public async Task Documents_AreListedForSeveralProjectsAtOnce()
    {
        var db = new ProjectDocumentsDbContext(new DbContextOptionsBuilder<ProjectDocumentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new ProjectDocumentRepository(db);
        foreach (var project in new[] { P1, P2, P3 })
        {
            await repository.AddAsync(new ProjectDocument(Guid.NewGuid(), Tenant, project, "doc", ProjectDocumentType.Other, "key", "a.pdf", "application/pdf", 1), default);
        }

        await db.SaveChangesAsync();
        var service = new ProjectDocumentService(repository, null!, db, new NoApprovals());

        var result = await service.ListForProjectsAsync(new ListProjectDocumentsForProjectsRequest([P1, P2]), default);

        Assert.Equal(new[] { P1, P2 }.OrderBy(x => x), result.Value!.Select(d => d.ProjectId).OrderBy(x => x));
        Assert.Empty((await service.ListForProjectsAsync(new ListProjectDocumentsForProjectsRequest([]), default)).Value!);
    }

    [Fact]
    public async Task WorkflowInstances_AreFoundBySubject_WithTheirDecisions_AndOnlyForThatTypeAndTenant()
    {
        var db = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var definitions = new WorkflowDefinitionRepository(db);
        var instances = new WorkflowInstanceRepository(db);
        var requester = new WorkflowApprovalRequester(definitions, instances, db);
        var approver = Guid.NewGuid();
        foreach (var subjectType in new[] { "Risk", "Stakeholder" })
        {
            var definition = new WorkflowDefinition(Guid.NewGuid(), Tenant, subjectType + " flow", subjectType);
            definition.AddStep(Guid.NewGuid(), "Step", approver, null);
            await definitions.AddAsync(definition, default);
        }

        await db.SaveChangesAsync();
        var risk1 = Guid.NewGuid();
        var risk2 = Guid.NewGuid();
        var stakeholder = Guid.NewGuid();
        await requester.RequestApprovalAsync(new ApprovalSubject("Risk", risk1, Tenant), default);
        await requester.RequestApprovalAsync(new ApprovalSubject("Risk", risk2, Tenant), default);
        await requester.RequestApprovalAsync(new ApprovalSubject("Stakeholder", stakeholder, Tenant), default);
        var service = new WorkflowInstanceService(instances, db, definitions, new WorkflowDelegationRepository(db), TimeProvider.System);

        var found = await service.ListForSubjectsAsync(Tenant, new ListInstancesForSubjectsRequest("Risk", [risk1, stakeholder, risk1]), default);

        var item = Assert.Single(found.Value!);
        Assert.Equal(risk1, item.SubjectId);
        Assert.Equal(approver, item.CurrentApproverUserId);

        Assert.Empty((await service.ListForSubjectsAsync(Guid.NewGuid(), new ListInstancesForSubjectsRequest("Risk", [risk1]), default)).Value!);
        Assert.Empty((await service.ListForSubjectsAsync(Tenant, new ListInstancesForSubjectsRequest("Risk", []), default)).Value!);
        Assert.Equal(2, (await service.ListForSubjectsAsync(Tenant, new ListInstancesForSubjectsRequest("Risk", [risk1, risk2]), default)).Value!.Count);
    }
}
