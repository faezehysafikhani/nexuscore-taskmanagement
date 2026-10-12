using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Application.EventHandlers;
using Nexus.ProjectManagement.Agile.Domain;
using Nexus.ProjectManagement.Agile.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

public sealed class AgileStatusSubmissionTests
{
    private sealed class FakeSubmissionRepository : IAgileTaskStatusSubmissionRepository
    {
        public List<AgileTaskStatusSubmission> Items { get; } = [];
        public Task<AgileTaskStatusSubmission?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(s => s.Id == id));
        public Task<IReadOnlyList<AgileTaskStatusSubmission>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgileTaskStatusSubmission>>(Items.Where(s => projectIds.Contains(s.ProjectId)).ToList());
        public Task<IReadOnlyList<AgileTaskStatusSubmission>> ListByTaskAsync(Guid taskId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgileTaskStatusSubmission>>(Items.Where(s => s.TaskId == taskId).ToList());
        public Task AddAsync(AgileTaskStatusSubmission submission, CancellationToken ct) { Items.Add(submission); return Task.CompletedTask; }
    }

    private sealed class SubmittingRequester : IApprovalRequester
    {
        public ApprovalSubject? Subject { get; private set; }
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct)
        {
            Subject = subject;
            return Task.FromResult(ApprovalRequestOutcome.Submitted);
        }
    }

    private static (AgileTaskStatusSubmissionService Service, FakeSubmissionRepository Submissions, AgileFixture Fixture) Build(IApprovalRequester requester)
    {
        var fixture = new AgileFixture();
        var submissions = new FakeSubmissionRepository();
        var service = new AgileTaskStatusSubmissionService(submissions, fixture.Tasks, fixture.TaskService, new FakeAgileUnitOfWork(), requester);
        return (service, submissions, fixture);
    }

    // ------------------------------------------------------------ status requests

    [Fact]
    public async Task WithoutAnApprovalBackend_TheTaskMovesAtOnce()
    {
        var (service, _, f) = Build(new NotConfiguredApprovalRequester());
        var task = await f.AddTaskAsync("A");

        var result = await service.SubmitAsync(new SubmitAgileTaskStatusRequest(AgileFixture.Tenant, f.ProjectId, task.Id, AgileTaskStatus.Done, "finished"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalStatus.Approved, result.Value!.ApprovalStatus);
        Assert.Equal(AgileTaskStatus.Done, f.Tasks.Items.Single().Status);
    }

    [Fact]
    public async Task WithAWorkflow_TheTaskStaysPut_UntilTheRequestIsApproved()
    {
        var requester = new SubmittingRequester();
        var (service, submissions, f) = Build(requester);
        var task = await f.AddTaskAsync("A");

        var submitted = (await service.SubmitAsync(new SubmitAgileTaskStatusRequest(AgileFixture.Tenant, f.ProjectId, task.Id, AgileTaskStatus.InProgress, null), default)).Value!;
        var again = await service.SubmitAsync(new SubmitAgileTaskStatusRequest(AgileFixture.Tenant, f.ProjectId, task.Id, AgileTaskStatus.Done, null), default);

        Assert.Equal(ApprovalStatus.PendingApproval, submitted.ApprovalStatus);
        Assert.Equal((AgileTaskStatus.ToDo, AgileTaskStatus.InProgress), (submitted.PreviousStatus, submitted.SubmittedStatus));
        Assert.Equal(("AgileTaskStatus", f.ProjectId), (requester.Subject!.SubjectType, requester.Subject.ScopeId));
        Assert.Equal(AgileTaskStatus.ToDo, f.Tasks.Items.Single().Status);
        Assert.Equal("conflict", again.Error.Code);

        await new AgileTaskStatusApprovalGrantedHandler(submissions, f.TaskService, new FakeAgileUnitOfWork())
            .HandleAsync(new ApprovalGranted("AgileTaskStatus", submitted.Id, AgileFixture.Tenant, null, null), default);

        Assert.Equal(AgileTaskStatus.InProgress, f.Tasks.Items.Single().Status);
        Assert.Equal(ApprovalStatus.Approved, submissions.Items.Single().ApprovalStatus);
    }

    [Fact]
    public async Task ARejectedRequest_LeavesTheTaskWhereItWas()
    {
        var (service, submissions, f) = Build(new SubmittingRequester());
        var task = await f.AddTaskAsync("A");
        var submitted = (await service.SubmitAsync(new SubmitAgileTaskStatusRequest(AgileFixture.Tenant, f.ProjectId, task.Id, AgileTaskStatus.Done, null), default)).Value!;

        await new AgileTaskStatusApprovalRejectedHandler(submissions, new FakeAgileUnitOfWork())
            .HandleAsync(new ApprovalRejected("AgileTaskStatus", submitted.Id, AgileFixture.Tenant, null, "not yet"), default);

        Assert.Equal(AgileTaskStatus.ToDo, f.Tasks.Items.Single().Status);
        Assert.Equal(ApprovalStatus.Rejected, submissions.Items.Single().ApprovalStatus);
    }

    [Fact]
    public async Task ARequest_IsRefusedForTheSameColumn_AnUnknownStatus_AndOtherProjects()
    {
        var (service, _, f) = Build(new NotConfiguredApprovalRequester());
        var task = await f.AddTaskAsync("A");

        var same = await service.SubmitAsync(new SubmitAgileTaskStatusRequest(AgileFixture.Tenant, f.ProjectId, task.Id, AgileTaskStatus.ToDo, null), default);
        var unknown = await service.SubmitAsync(new SubmitAgileTaskStatusRequest(AgileFixture.Tenant, f.ProjectId, task.Id, (AgileTaskStatus)99, null), default);
        var elsewhere = await service.SubmitAsync(new SubmitAgileTaskStatusRequest(AgileFixture.Tenant, Guid.NewGuid(), task.Id, AgileTaskStatus.Done, null), default);

        Assert.Equal("validation.error", same.Error.Code);
        Assert.Equal("validation.error", unknown.Error.Code);
        Assert.Equal("not_found", elsewhere.Error.Code);
    }

    [Fact]
    public async Task OtherSubjectTypes_AreIgnoredByTheStatusHandlers()
    {
        var (_, submissions, f) = Build(new SubmittingRequester());
        var task = await f.AddTaskAsync("A");

        await new AgileTaskStatusApprovalGrantedHandler(submissions, f.TaskService, new FakeAgileUnitOfWork())
            .HandleAsync(new ApprovalGranted("AgileTask", task.Id, AgileFixture.Tenant, null, null), default);

        Assert.Equal(AgileTaskStatus.ToDo, f.Tasks.Items.Single().Status);
    }

    // ------------------------------------------------------------ several projects, sprint count

    [Fact]
    public async Task TasksAndSprints_CanBeListedForSeveralProjectsAtOnce()
    {
        var f = new AgileFixture();
        await f.AddTaskAsync("One");
        await f.AddSprintAsync();
        var other = Guid.NewGuid();
        await f.TaskService.CreateAsync(new CreateAgileTaskRequest(AgileFixture.Tenant, other, "Two", null, null, null, null, AgileTaskPriority.Low, null), default);
        await f.SprintService.CreateAsync(new CreateSprintRequest(AgileFixture.Tenant, other, null, null, null, null), default);

        var tasks = (await f.TaskService.ListForProjectsAsync(new ListAgileForProjectsRequest([f.ProjectId, other, other]), default)).Value!;
        var sprints = (await f.SprintService.ListForProjectsAsync(new ListAgileForProjectsRequest([other]), default)).Value!;

        Assert.Equal(["One", "Two"], tasks.Select(t => t.Title).Order());
        Assert.Single(sprints);
        Assert.Empty((await f.TaskService.ListForProjectsAsync(new ListAgileForProjectsRequest([]), default)).Value!);
    }

    [Fact]
    public async Task SetSprintCount_CreatesTheMissingSprints_AndTrimsOnlyEmptyPlannedOnes()
    {
        var f = new AgileFixture();

        var three = (await f.SprintService.SetCountAsync(new SetSprintCountRequest(AgileFixture.Tenant, f.ProjectId, 3), default)).Value!;
        var same = (await f.SprintService.SetCountAsync(new SetSprintCountRequest(AgileFixture.Tenant, f.ProjectId, 3), default)).Value!;
        await f.AddTaskAsync("Busy", sprint: 3);
        var blocked = await f.SprintService.SetCountAsync(new SetSprintCountRequest(AgileFixture.Tenant, f.ProjectId, 2), default);
        var two = await f.SprintService.SetCountAsync(new SetSprintCountRequest(AgileFixture.Tenant, f.ProjectId, 5), default);
        var tooMany = await f.SprintService.SetCountAsync(new SetSprintCountRequest(AgileFixture.Tenant, f.ProjectId, 500), default);

        Assert.Equal([1, 2, 3], three.Select(s => s.Number));
        Assert.Equal(3, same.Count); // nothing duplicated
        Assert.Equal("conflict", blocked.Error.Code); // sprint 3 still holds a task
        Assert.Equal(3, f.Sprints.Items.Count(s => s.Number <= 3)); // the refusal changed nothing
        Assert.Equal([1, 2, 3, 4, 5], two.Value!.Select(s => s.Number));
        Assert.Equal("validation.error", tooMany.Error.Code);

        // Remove the task, then the surplus sprints can go.
        await f.TaskService.DeleteAsync(f.Tasks.Items.Single().Id, default);
        var one = await f.SprintService.SetCountAsync(new SetSprintCountRequest(AgileFixture.Tenant, f.ProjectId, 1), default);
        Assert.Equal([1], one.Value!.Select(s => s.Number));
    }

    // ------------------------------------------------------------ schema

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = new AgileDbContext(
            new DbContextOptionsBuilder<AgileDbContext>()
                .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True").Options);

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "agile_planning", "2026-10-13-add-agile-status-submissions.sql", typeof(AgileTaskStatusSchemaUpgrade),
            ["AgileTaskStatusSubmissions"]);
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-13-add-agile-status-submissions.sql", typeof(AgileTaskStatusSchemaUpgrade));
    }
}
