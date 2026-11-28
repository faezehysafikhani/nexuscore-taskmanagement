using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Core.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>Phase 3: a project's work calendar must exist in its tenant (M2); how many approved projects a calendar moves («تصمیم ۴»).</summary>
public sealed class ProjectCalendarRulesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Ours = Guid.NewGuid();
    private static readonly Guid Theirs = Guid.NewGuid(); // a calendar of another organization

    private sealed class Calendars : IProjectCalendarLookup
    {
        public Task<bool> ExistsAsync(Guid tenantId, Guid calendarId, CancellationToken ct) => Task.FromResult(tenantId == Tenant && calendarId == Ours);
    }

    private sealed class NoApproval : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private static (ProjectService Service, ProjectManagementCoreDbContext Db) Build()
    {
        var db = new ProjectManagementCoreDbContext(new DbContextOptionsBuilder<ProjectManagementCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new ProjectService(new ProjectRepository(db), db, new NoApproval(), calendarLookup: new Calendars()), db);
    }

    private static CreateProjectRequest Create(Guid? calendar, string code) =>
        new(Tenant, "پروژه " + code, code, ProjectType.Waterfall, null, null, null, calendar, null, null, null, null, null, null, null, null, null);

    [Fact]
    public async Task M2_ACalendarOfAnotherTenant_OrOneThatDoesNotExist_IsRefused_OnCreateCheckAndUpdate()
    {
        var (service, db) = Build();
        await using var _ = db;

        Assert.Equal("project.calendar_not_found", (await service.CreateAsync(Create(Theirs, "A"), default)).Error.Code);
        Assert.Equal("project.calendar_not_found", (await service.CheckAsync(Create(Guid.NewGuid(), "A"), null, default)).Error.Code);

        var created = (await service.CreateAsync(Create(Ours, "B"), default)).Value!;
        Assert.Equal(Ours, created.WorkCalendarId);
        Assert.True((await service.CreateAsync(Create(null, "C"), default)).IsSuccess); // no calendar chosen: nothing to check

        var moved = new UpdateProjectRequest(created.Name, created.Code, null, null, null, Theirs, null, null, null, null, null, null, null, null, null);
        Assert.Equal("project.calendar_not_found", (await service.UpdateAsync(created.Id, moved, default)).Error.Code);
    }

    [Fact]
    public async Task Decision4_TheCalendarUsage_CountsTheProjectsAndTheApprovedOnes()
    {
        var (service, db) = Build();
        await using var _ = db;
        var a = (await service.CreateAsync(Create(Ours, "A"), default)).Value!;
        await service.CreateAsync(Create(Ours, "B"), default);
        await service.CreateAsync(Create(null, "C"), default);
        await service.SubmitForApprovalAsync(a.Id, default); // no workflow: approved at once

        var usage = (await service.GetCalendarUsageAsync(Tenant, Ours, default)).Value!;

        Assert.Equal((2, 1), (usage.Projects, usage.ApprovedProjects));
        Assert.Equal(0, (await service.GetCalendarUsageAsync(Guid.NewGuid(), Ours, default)).Value!.Projects); // another tenant sees nothing
    }
}
