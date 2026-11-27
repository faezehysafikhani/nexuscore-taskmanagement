using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;

namespace Nexus.CompositionTests;

/// <summary>
/// Submit for review (phase 4). This host has no approval backend, so a submission completes the task; the pending
/// state - which the Workflow module drives - is set up directly and decided through the same events Workflow raises.
/// </summary>
public sealed class TaskReviewTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Tasks = "/api/task-management/tasks";
    private static readonly string[] Permissions = [TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(AccessControlTests.Caller User, Guid Id)> NewTaskAsync()
    {
        var user = await host.UserAsync(host.TenantA, Permissions);
        var created = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/quick", new { title = "Check me" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (user, (await Json(created)).GetProperty("id").GetGuid());
    }

    private async Task PutInReviewAsync(Guid id, Guid by)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        var task = await db.Tasks.IgnoreQueryFilters().FirstAsync(t => t.Id == id);
        task.SubmitForReview(by, "please check", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task DecideAsync(Guid tenantId, Guid id, bool approve)
    {
        using var scope = host.Services.CreateScope();
        if (approve)
        {
            foreach (var h in scope.ServiceProvider.GetServices<IDomainEventHandler<ApprovalGranted>>())
                await h.HandleAsync(new ApprovalGranted("TaskReview", id, tenantId, null, "ok"), default);
        }
        else
        {
            foreach (var h in scope.ServiceProvider.GetServices<IDomainEventHandler<ApprovalRejected>>())
                await h.HandleAsync(new ApprovalRejected("TaskReview", id, tenantId, null, "not yet"), default);
        }
    }

    [Fact]
    public async Task WithNoApprovalChain_SubmittingCompletesTheTask()
    {
        var (user, id) = await NewTaskAsync();
        var response = await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/submit-review", new { comment = "done" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("Completed", body.GetProperty("status").GetString());
        Assert.Equal("Approved", body.GetProperty("reviewStatus").GetString());

        // Already done: nothing more to submit.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/submit-review", new { })).StatusCode);
    }

    [Fact]
    public async Task WhileInReview_TheTaskCannotBeCompletedByHand_OrSubmittedAgain()
    {
        var (user, id) = await NewTaskAsync();
        await PutInReviewAsync(id, user.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(user, HttpMethod.Patch, $"{Tasks}/{id}/status", new { status = "Completed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/submit-review", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Patch, $"{Tasks}/{id}/status", new { status = "InProgress" })).StatusCode);

        var waiting = await Json(await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/in-review", new { ids = new[] { id, Guid.NewGuid() } }));
        Assert.Equal(1, waiting.GetArrayLength());
        Assert.Equal(id, waiting[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task AnApprovedReview_CompletesTheTask()
    {
        var (user, id) = await NewTaskAsync();
        await PutInReviewAsync(id, user.Id);
        await DecideAsync(host.TenantA, id, approve: true);

        var task = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{id}"));
        Assert.Equal("Completed", task.GetProperty("status").GetString());
        Assert.Equal("Approved", task.GetProperty("reviewStatus").GetString());
        Assert.Equal("ok", task.GetProperty("reviewComment").GetString());
    }

    [Fact]
    public async Task ARejectedReview_SendsTheTaskBackToWork_AndItCanBeSubmittedAgain()
    {
        var (user, id) = await NewTaskAsync();
        await PutInReviewAsync(id, user.Id);
        await DecideAsync(host.TenantA, id, approve: false);

        var task = await Json(await host.SendAsync(user, HttpMethod.Get, $"{Tasks}/{id}"));
        Assert.Equal("InProgress", task.GetProperty("status").GetString());
        Assert.Equal("Rejected", task.GetProperty("reviewStatus").GetString());
        Assert.Equal("not yet", task.GetProperty("reviewComment").GetString());

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(user, HttpMethod.Post, $"{Tasks}/{id}/submit-review", new { })).StatusCode);
    }

    [Fact]
    public async Task OnlyThePeopleOfTheTask_CanSubmit()
    {
        var (_, id) = await NewTaskAsync();
        var stranger = await host.UserAsync(host.TenantA, Permissions);
        Assert.NotEqual(HttpStatusCode.OK, (await host.SendAsync(stranger, HttpMethod.Post, $"{Tasks}/{id}/submit-review", new { })).StatusCode);
    }
}
