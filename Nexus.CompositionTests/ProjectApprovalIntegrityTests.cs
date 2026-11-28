using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Bundles;
using Nexus.Integrations.ProjectWorkflow.Application;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Core.Infrastructure;
using Nexus.Workflow;
using Nexus.Workflow.Application;
using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using Nexus.Workflow.Infrastructure;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Interfaces;
using Notifications.Application.Abstractions;

namespace Nexus.CompositionTests;

/// <summary>
/// Phase 1 of the PM fixes: the project charter's approval cycle (H2-H6, M1, M6). The project and Workflow run on their
/// own stores, as in production, wired with the real requester, tracker, participant resolver and decision service.
/// </summary>
public sealed class ProjectApprovalIntegrityTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();   // the project manager, who usually submits
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Boss = Guid.NewGuid();      // the manager's manager in the org chart
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();

    private sealed class SignedIn(Guid? user) : ICurrentUserContext
    {
        public Guid? UserId => user;
        public Guid? TenantId => Tenant;
        public string? Email => null;
        public string? IpAddress => null;
    }

    private sealed class OrgChart(Guid? manager) : IApprovalKeyResolver
    {
        public Task<Guid?> ResolveAsync(ApprovalSubject subject, Guid? submitterUserId, string key, CancellationToken ct) =>
            Task.FromResult(key == ApprovalParticipants.OrgManager ? manager : null);
    }

    /// <summary>The project store; can be told to fail its next save (the case an approval must not be orphaned by).</summary>
    private sealed class FailingSaves(ProjectManagementCoreDbContext db) : IProjectManagementUnitOfWork
    {
        public bool FailNext { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("database is down");
            }

            return db.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class Fixture
    {
        private readonly string _workflowStore = Guid.NewGuid().ToString();
        public ProjectManagementCoreDbContext ProjectDb { get; } =
            new(new DbContextOptionsBuilder<ProjectManagementCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public WorkflowDbContext Db { get; }
        public ProjectRepository Projects { get; }
        public FailingSaves ProjectSaves { get; }
        public WorkflowDefinitionService Definitions { get; }
        public Guid? OrgManager { get; set; } = Boss;

        public Fixture()
        {
            Db = NewWorkflowContext();
            Projects = new ProjectRepository(ProjectDb);
            ProjectSaves = new FailingSaves(ProjectDb);
            Definitions = new WorkflowDefinitionService(new WorkflowDefinitionRepository(Db), Db);
        }

        public WorkflowDbContext NewWorkflowContext() =>
            new(new DbContextOptionsBuilder<WorkflowDbContext>().UseInMemoryDatabase(_workflowStore).Options);

        public WorkflowApprovalRequester Requester(Guid? submitter) => new(
            new WorkflowDefinitionRepository(Db), new WorkflowInstanceRepository(Db), Db,
            [new ProjectApprovalParticipantResolver(Projects)], [new OrgChart(OrgManager)], new SignedIn(submitter));

        public ProjectService Service(Guid? submitter = null, bool workflow = true) => new(
            Projects, ProjectSaves,
            workflow ? Requester(submitter ?? Manager) : new NoApproval(),
            currentUser: new SignedIn(submitter ?? Manager),
            approvalTracker: workflow ? new WorkflowApprovalTracker(new WorkflowInstanceRepository(Db), Db) : null);

        public WorkflowInstanceService Center(WorkflowDbContext? db = null)
        {
            var context = db ?? Db;
            return new WorkflowInstanceService(new WorkflowInstanceRepository(context), context, new WorkflowDefinitionRepository(context));
        }

        /// <summary>The general charter chain (as «اعمال تنظیمات استاندارد» sets it): the project manager approves.</summary>
        public Task DefineGeneral(params AddWorkflowStepRequest[] steps) =>
            Definitions.SetStepsAsync(new SetWorkflowStepsRequest(Tenant, "Project", null, null, null,
                steps.Length > 0 ? steps : [new AddWorkflowStepRequest("تایید نهایی", null, null, ApprovalParticipants.Manager)]), default);

        public Task DefineForProject(Guid projectId, params AddWorkflowStepRequest[] steps) =>
            Definitions.SetStepsAsync(new SetWorkflowStepsRequest(Tenant, "Project", "Project", projectId, null, steps), default);

        public async Task<Project> NewProject(Guid? manager = null)
        {
            var project = new Project(Guid.NewGuid(), Tenant, "پروژه", "P" + Guid.NewGuid().ToString("N")[..5], ProjectType.Waterfall, manager ?? Manager, Owner);
            project.UpdateDetails(project.Name, project.Code, manager ?? Manager, Owner, Unit, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1),
                1000, "هدف پروژه", "نیازمندی‌ها", null, null, null, null);
            await Projects.AddAsync(project, default);
            await ProjectDb.SaveChangesAsync();
            return project;
        }

        public Task<WorkflowInstance> InstanceOf(Guid projectId) =>
            Db.WorkflowInstances.Include(i => i.Decisions).SingleAsync(i => i.SubjectId == projectId && i.Status == WorkflowInstanceStatus.InProgress);
    }

    private sealed class NoApproval : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) =>
            Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private static UpdateProjectRequest EditOf(Project p, Guid? manager = null, DateOnly? end = null, string? description = null) => new(
        p.Name, p.Code, manager ?? p.ManagerUserId, p.OwnerUserId, p.OrganizationUnitId, p.WorkCalendarId, p.StartDate, end ?? p.EndDate, p.Cost,
        p.Goal, p.Requirements, p.Constraints, p.Assumptions, description ?? p.Description, p.Charter);

    // ------------------------------------------------------------------ H2: status of an unapproved project

    [Theory]
    [InlineData(ProjectStatus.Active)]
    [InlineData(ProjectStatus.OnHold)]
    [InlineData(ProjectStatus.Completed)]
    public async Task H2_AnUnapprovedProject_CannotTakeAWorkingStatus(ProjectStatus status)
    {
        var f = new Fixture();
        var project = await f.NewProject();

        var refused = await f.Service(workflow: false).ChangeStatusAsync(project.Id, new ChangeProjectStatusRequest(status), default);

        Assert.Equal("project.not_approved", refused.Error.Code);
        Assert.Equal(ProjectStatus.Draft, project.Status);
        Assert.True((await f.Service(workflow: false).ChangeStatusAsync(project.Id, new ChangeProjectStatusRequest(ProjectStatus.Draft), default)).IsSuccess);
    }

    [Fact]
    public async Task H2_OnceApproved_TheWorkingStatusesAreOpen()
    {
        var f = new Fixture();
        var project = await f.NewProject();
        Assert.Equal(ApprovalStatus.Approved, (await f.Service(workflow: false).SubmitForApprovalAsync(project.Id, default)).Value!.ApprovalStatus);

        Assert.Equal(ProjectStatus.OnHold, (await f.Service(workflow: false).ChangeStatusAsync(project.Id, new ChangeProjectStatusRequest(ProjectStatus.OnHold), default)).Value!.Status);
    }

    // ------------------------------------------------------------------ H5: sending again

    [Fact]
    public async Task H5_APendingOrApprovedProject_IsNotSentAgain_AndNoSecondApprovalStarts()
    {
        var f = new Fixture();
        await f.DefineGeneral(new AddWorkflowStepRequest("بررسی", Approver, null));
        var project = await f.NewProject();

        Assert.Equal(ApprovalStatus.PendingApproval, (await f.Service().SubmitForApprovalAsync(project.Id, default)).Value!.ApprovalStatus);
        Assert.Equal("project.already_pending", (await f.Service().SubmitForApprovalAsync(project.Id, default)).Error.Code);
        Assert.Single(f.Db.WorkflowInstances);

        var instance = await f.InstanceOf(project.Id);
        Assert.True((await f.Center().ApproveAsync(instance.Id, Approver, new DecideWorkflowInstanceRequest(null), default)).IsSuccess);
        await f.Service().SyncApprovalAsync(project.Id, default); // the handler is not wired here; the sync applies the decision

        Assert.Equal("project.already_approved", (await f.Service().SubmitForApprovalAsync(project.Id, default)).Error.Code);
    }

    [Fact]
    public async Task H5_TwoDecisionsOnTheSameStepAtOnce_OnlyTheFirstIsSaved()
    {
        // A relational store (SQLite), as the concurrency token is checked by the UPDATE's WHERE clause.
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        WorkflowDbContext NewContext() => new(new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlite(connection).Options);
        await using (var setup = NewContext())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.WorkflowInstances.Add(new WorkflowInstance(Guid.NewGuid(), Tenant, Guid.NewGuid(), "Project", Guid.NewGuid(),
                [new WorkflowInstanceStep(1, "بررسی", Approver)], Manager));
            await setup.SaveChangesAsync();
        }

        await using var first = NewContext();
        await using var second = NewContext();
        var id = (await first.WorkflowInstances.SingleAsync()).Id;
        // Both requests have read the approval before either saves.
        await second.WorkflowInstances.Include(i => i.Decisions).SingleAsync(i => i.Id == id);
        var Center = (WorkflowDbContext db) => new WorkflowInstanceService(new WorkflowInstanceRepository(db), db, new WorkflowDefinitionRepository(db));

        var approved = await Center(first).ApproveAsync(id, Approver, new DecideWorkflowInstanceRequest("ok"), default);
        var rejected = await Center(second).RejectAsync(id, Approver, new DecideWorkflowInstanceRequest("no"), default);

        Assert.True(approved.IsSuccess);
        Assert.Equal("workflow.concurrent_decision", rejected.Error.Code);
        await using var check = NewContext();
        var saved = await check.WorkflowInstances.Include(i => i.Decisions).SingleAsync(i => i.Id == id);
        Assert.Equal(WorkflowInstanceStatus.Approved, saved.Status);
        Assert.Single(saved.Decisions);
    }

    // ------------------------------------------------------------------ H3 / «تصمیم ۲»: editing the charter

    [Fact]
    public async Task H3_ACharterWithTheApprovers_CannotBeEdited()
    {
        var f = new Fixture();
        await f.DefineGeneral(new AddWorkflowStepRequest("بررسی", Approver, null));
        var project = await f.NewProject();
        await f.Service().SubmitForApprovalAsync(project.Id, default);

        var refused = await f.Service().UpdateAsync(project.Id, EditOf(project, description: "تغییر"), default);

        Assert.Equal("project.pending_approval", refused.Error.Code);
    }

    [Fact]
    public async Task Decision2_AfterApproval_KeyChangesSendTheCharterBack_OtherChangesDoNot()
    {
        var f = new Fixture();
        await f.DefineGeneral(new AddWorkflowStepRequest("بررسی", Approver, null));
        var project = await f.NewProject();
        await f.Service().SubmitForApprovalAsync(project.Id, default);
        await f.Center().ApproveAsync((await f.InstanceOf(project.Id)).Id, Approver, new DecideWorkflowInstanceRequest(null), default);
        await f.Service().SyncApprovalAsync(project.Id, default);
        Assert.Equal(ProjectStatus.Active, project.Status);

        var free = await f.Service().UpdateAsync(project.Id, EditOf(project, description: "توضیح تازه"), default);
        Assert.Equal(ApprovalStatus.Approved, free.Value!.ApprovalStatus);

        var key = await f.Service().UpdateAsync(project.Id, EditOf(project, end: new DateOnly(2026, 9, 1)), default);
        Assert.Equal(ApprovalStatus.PendingApproval, key.Value!.ApprovalStatus);
        Assert.Equal(ProjectStatus.Active, key.Value.Status); // the work goes on meanwhile
        Assert.Equal(new DateOnly(2026, 9, 1), key.Value.EndDate);
        Assert.NotNull(await f.InstanceOf(project.Id)); // the charter's approval runs again
    }

    // ------------------------------------------------------------------ M1: the project's own charter chain

    [Fact]
    public async Task M1_AChainSetForTheProject_WinsOverTheGeneralOne_AndTheGeneralOneStillApplies()
    {
        var f = new Fixture();
        var special = Guid.NewGuid();
        await f.DefineGeneral(new AddWorkflowStepRequest("عمومی", Approver, null));
        var scoped = await f.NewProject();
        var plain = await f.NewProject();
        await f.DefineForProject(scoped.Id, new AddWorkflowStepRequest("ویژه", special, null));

        await f.Service().SubmitForApprovalAsync(scoped.Id, default);
        await f.Service().SubmitForApprovalAsync(plain.Id, default);

        Assert.Equal(special, (await f.InstanceOf(scoped.Id)).GetSteps()!.Single().ApproverUserId);
        Assert.Equal(Approver, (await f.InstanceOf(plain.Id)).GetSteps()!.Single().ApproverUserId);
    }

    // ------------------------------------------------------------------ H6 / «تصمیم ۳»: no self-approval

    [Fact]
    public async Task H6_AManagerSubmittingTheirOwnCharter_HasItGoToTheirOrgManager()
    {
        var f = new Fixture();
        await f.DefineGeneral(); // the standard chain: the project manager approves
        var project = await f.NewProject();

        var sent = await f.Service(submitter: Manager).SubmitForApprovalAsync(project.Id, default);

        Assert.Equal(ApprovalStatus.PendingApproval, sent.Value!.ApprovalStatus);
        var instance = await f.InstanceOf(project.Id);
        Assert.Equal(Boss, instance.GetSteps()!.Single().ApproverUserId);
        Assert.Equal(Manager, instance.SubmittedByUserId);
    }

    [Fact]
    public async Task H6_WithNoOrgManager_TheSubmissionIsRefused_AndNothingIsStarted()
    {
        var f = new Fixture { OrgManager = null };
        await f.DefineGeneral();
        var project = await f.NewProject();

        var refused = await f.Service(submitter: Manager).SubmitForApprovalAsync(project.Id, default);

        Assert.Equal("workflow.self_approval", refused.Error.Code);
        Assert.Contains("تایید نهایی", refused.Error.Message);
        Assert.Equal(ApprovalStatus.NotSubmitted, project.ApprovalStatus);
        Assert.Empty(f.Db.WorkflowInstances);
    }

    [Fact]
    public async Task H6_TheSubmitterCannotDecide_EvenAnOpenStep_OrAsAnAdministrator()
    {
        var f = new Fixture();
        await f.DefineGeneral(new AddWorkflowStepRequest("هر تأییدکننده", null, null)); // open to anyone with the permission
        var project = await f.NewProject();
        await f.Service(submitter: Owner).SubmitForApprovalAsync(project.Id, default);
        var instance = await f.InstanceOf(project.Id);

        var self = await f.Center().ApproveAsync(instance.Id, Owner, new DecideWorkflowInstanceRequest(null), canActForAnyone: true, default);

        Assert.Equal("workflow.self_approval", self.Error.Code);
        Assert.True((await f.Center().ApproveAsync(instance.Id, Approver, new DecideWorkflowInstanceRequest(null), default)).IsSuccess);
    }

    // ------------------------------------------------------------------ L: a mandatory step nobody fills

    [Fact]
    public async Task L_AMandatoryStepWithoutAnApprover_RefusesTheSubmissionWithItsName()
    {
        var f = new Fixture();
        await f.DefineGeneral(new AddWorkflowStepRequest("تأیید مالک", null, null, ApprovalParticipants.Approver2));
        var project = await f.NewProject();

        var refused = await f.Service(submitter: Owner).SubmitForApprovalAsync(project.Id, default);

        Assert.Equal("workflow.approver_missing", refused.Error.Code);
        Assert.Contains("تأیید مالک", refused.Error.Message);
        Assert.Empty(f.Db.WorkflowInstances);
    }

    // ------------------------------------------------------------------ H4: the two stores stay consistent

    [Fact]
    public async Task H4_IfTheProjectCannotBeSaved_TheApprovalJustStartedIsWithdrawn()
    {
        var f = new Fixture();
        await f.DefineGeneral(new AddWorkflowStepRequest("بررسی", Approver, null));
        var project = await f.NewProject();
        f.ProjectSaves.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service().SubmitForApprovalAsync(project.Id, default));

        await using var check = f.NewWorkflowContext();
        Assert.Equal(WorkflowInstanceStatus.Withdrawn, (await check.WorkflowInstances.SingleAsync()).Status);
        Assert.Empty((await f.Center().ListPendingForApproverAsync(Tenant, Approver, default)).Value!);
    }

    [Fact]
    public async Task H4_AProjectLeftPendingAfterADecision_IsRepaired_AndCanBeSentAgainAfterARejection()
    {
        var f = new Fixture();
        await f.DefineGeneral(new AddWorkflowStepRequest("بررسی", Approver, null));
        var project = await f.NewProject();
        await f.Service().SubmitForApprovalAsync(project.Id, default);

        // The decision is committed, but the project's handler never ran (it failed): the project still says "pending".
        await f.Center().RejectAsync((await f.InstanceOf(project.Id)).Id, Approver, new DecideWorkflowInstanceRequest("هزینه بیش از حد است"), default);
        Assert.Equal(ApprovalStatus.PendingApproval, project.ApprovalStatus);

        Assert.Equal(ApprovalStatus.Rejected, (await f.Service().GetAsync(project.Id, default)).Value!.ApprovalStatus);
        Assert.Equal(ApprovalStatus.PendingApproval, (await f.Service().SubmitForApprovalAsync(project.Id, default)).Value!.ApprovalStatus);
    }

    [Fact]
    public async Task H4_APendingProjectWithNoApprovalAtAll_GoesBackToNotSubmitted()
    {
        var f = new Fixture();
        var project = await f.NewProject();
        project.MarkPendingApproval(); // the approval never reached Workflow
        await f.ProjectDb.SaveChangesAsync();

        Assert.Equal(ApprovalStatus.NotSubmitted, (await f.Service().SyncApprovalAsync(project.Id, default)).Value!.ApprovalStatus);
    }

    // ------------------------------------------------------------------ M6: notifications

    private sealed class Inbox : INotificationService
    {
        public List<NotificationRequest> Sent { get; } = [];
        public Task NotifyAsync(Guid userId, string title, string message, string type, CancellationToken cancellationToken = default, Guid? tenantId = null) =>
            NotifyAsync(new NotificationRequest(userId, title, message, type, tenantId), cancellationToken);
        public Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default) { Sent.Add(request); return Task.CompletedTask; }
    }

    private sealed class Names : IApprovalSubjectDescriber
    {
        public Task<string?> DescribeAsync(string subjectType, Guid subjectId, CancellationToken ct) => Task.FromResult<string?>("ساختمان مرکزی");
    }

    [Fact]
    public async Task M6_TheApproverIsToldItWaits_AndTheSubmitterIsToldTheDecisionWithItsReason()
    {
        var inbox = new Inbox();
        var services = new ServiceCollection().AddSingleton<INotificationService>(inbox).AddSingleton<IApprovalSubjectDescriber, Names>().BuildServiceProvider();
        var notices = new ApprovalNotifications(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ApprovalNotifications>.Instance);
        var project = Guid.NewGuid();

        await notices.HandleAsync(new ApprovalStepAssigned("Project", project, Tenant, 1, "تایید نهایی", Approver, Manager), default);
        await notices.HandleAsync(new ApprovalRejected("Project", project, Tenant, Approver, "هزینه بیش از حد است", Manager), default);
        await notices.HandleAsync(new ApprovalStepAssigned("Risk", Guid.NewGuid(), Tenant, 1, "x", Approver, Manager), default); // not a charter

        Assert.Equal(2, inbox.Sent.Count);
        Assert.Equal(Approver, inbox.Sent[0].UserId);
        Assert.Contains("منشور پروژه «ساختمان مرکزی»", inbox.Sent[0].Message);
        Assert.Equal(Manager, inbox.Sent[1].UserId);
        Assert.Contains("هزینه بیش از حد است", inbox.Sent[1].Message);
        Assert.All(inbox.Sent, n => Assert.Equal(project, n.ReferenceId));
    }

    [Fact]
    public async Task M6_TheWorkflowRaisesAStepEvent_ForTheFirstStepAndEachNextOne()
    {
        var steps = new List<WorkflowInstanceStep> { new(1, "اول", Approver), new(2, "دوم", Boss) };
        var instance = new WorkflowInstance(Guid.NewGuid(), Tenant, Guid.NewGuid(), "Project", Guid.NewGuid(), steps, Manager);

        Assert.Equal(Approver, Assert.IsType<ApprovalStepAssigned>(Assert.Single(instance.DomainEvents)).ApproverUserId);
        instance.ClearDomainEvents();
        instance.Decide(Approver, true, null);
        Assert.Equal(Boss, Assert.IsType<ApprovalStepAssigned>(Assert.Single(instance.DomainEvents)).ApproverUserId);
        instance.ClearDomainEvents();
        instance.Decide(Boss, true, null);
        Assert.Equal(Manager, Assert.IsType<ApprovalGranted>(Assert.Single(instance.DomainEvents)).SubmittedByUserId);
        await Task.CompletedTask;
    }

    // ------------------------------------------------------------------ the schema upgrade

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel_AndOnlyAddThings()
    {
        using var db = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True").Options);

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "workflow", "2026-11-28-add-workflow-approval-integrity.sql", typeof(WorkflowApprovalIntegritySchemaUpgrade),
            [],
            ("WorkflowInstances", "[SubmittedByUserId] uniqueidentifier NULL"),
            ("WorkflowInstances", "[RowVersion] uniqueidentifier NULL"));
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-11-28-add-workflow-approval-integrity.sql", typeof(WorkflowApprovalIntegritySchemaUpgrade));
    }
}
