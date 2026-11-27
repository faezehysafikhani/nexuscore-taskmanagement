using System.Net;
using System.Text.Json;
using Nexus.Calendar.Permissions;
using Nexus.TaskManagement.Permissions;

namespace Nexus.CompositionTests;

/// <summary>Minutes to tasks (phase 8): the action items of a meeting become tasks that keep the meeting as their source.</summary>
public sealed class MeetingActionTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private const string Meetings = "/api/calendar/meetings";
    private static readonly string[] Permissions =
    [
        CalendarPermissions.ViewMeetings, CalendarPermissions.CreateMeetings, CalendarPermissions.EditMeetings,
        TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit
    ];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(AccessControlTests.Caller Organizer, JsonElement Meeting)> NewMeetingAsync(object[] actions)
    {
        var organizer = await host.UserAsync(host.TenantA, Permissions);
        var created = await host.SendAsync(organizer, HttpMethod.Post, Meetings, new
        {
            title = "Weekly sync " + Guid.NewGuid().ToString("N")[..6], type = "meeting", date = "2030-03-12", startTime = "09:00", endTime = "10:00",
            organizerUserId = organizer.Id, participantIds = Array.Empty<Guid>(), agenda = Array.Empty<object>(),
            minutes = "Decided two things.", actions, taskIds = Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        return (organizer, await Json(created));
    }

    [Fact]
    public async Task OneAction_BecomesATask_ThatKeepsTheMeeting_AndTheActionKeepsTheTask()
    {
        var assignee = await host.UserAsync(host.TenantA, Permissions);
        var (organizer, meeting) = await NewMeetingAsync([
            new { title = "Send the report", assigneeUserId = assignee.Id, dueDate = "2030-03-20", status = "todo" },
            new { title = "Book the room", status = "todo" }]);
        var id = meeting.GetProperty("id").GetGuid();

        var response = await host.SendAsync(organizer, HttpMethod.Post, $"{Meetings}/{id}/actions/0/convert-to-task");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Json(response);
        var taskId = result.GetProperty("converted")[0].GetProperty("taskId").GetGuid();

        // The meeting now points to the task - from the action, and in its task list.
        var actions = result.GetProperty("meeting").GetProperty("details").GetProperty("actions");
        Assert.Equal(taskId, actions[0].GetProperty("taskId").GetGuid());
        Assert.Equal(JsonValueKind.Null, actions[1].GetProperty("taskId").ValueKind);
        Assert.Contains(result.GetProperty("meeting").GetProperty("details").GetProperty("taskIds").EnumerateArray(), t => t.GetGuid() == taskId);

        // The task is the assignee's, due when the action says, and knows where it came from.
        var task = await Json(await host.SendAsync(assignee, HttpMethod.Get, $"/api/task-management/tasks/{taskId}"));
        Assert.Equal("Send the report", task.GetProperty("title").GetString());
        Assert.Equal("2030-03-20", task.GetProperty("dueDate").GetString());
        Assert.Equal("MeetingAction", task.GetProperty("sourceType").GetString());
        Assert.Equal(id, task.GetProperty("sourceId").GetGuid());

        // Once only.
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(organizer, HttpMethod.Post, $"{Meetings}/{id}/actions/0/convert-to-task")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(organizer, HttpMethod.Post, $"{Meetings}/{id}/actions/9/convert-to-task")).StatusCode);
    }

    [Fact]
    public async Task ConvertAll_TakesTheRest_AndNothingIsLeftToConvertAfterwards()
    {
        var (organizer, meeting) = await NewMeetingAsync([
            new { title = "One", status = "todo" }, new { title = "Two", status = "todo" }, new { title = "Three", status = "todo" }]);
        var id = meeting.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(organizer, HttpMethod.Post, $"{Meetings}/{id}/actions/1/convert-to-task")).StatusCode);

        var all = await host.SendAsync(organizer, HttpMethod.Post, $"{Meetings}/{id}/actions/convert-all");
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.Equal(2, (await Json(all)).GetProperty("converted").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(organizer, HttpMethod.Post, $"{Meetings}/{id}/actions/convert-all")).StatusCode);

        var read = await Json(await host.SendAsync(organizer, HttpMethod.Get, $"{Meetings}/{id}"));
        Assert.All(read.GetProperty("details").GetProperty("actions").EnumerateArray(), a => Assert.NotEqual(JsonValueKind.Null, a.GetProperty("taskId").ValueKind));
        Assert.Equal(3, read.GetProperty("details").GetProperty("taskIds").GetArrayLength());
    }

    [Fact]
    public async Task OnlyTheOneWhoRunsTheMeeting_AndWhoMayCreateTasks_CanConvert()
    {
        var (organizer, meeting) = await NewMeetingAsync([new { title = "Private", status = "todo" }]);
        var id = meeting.GetProperty("id").GetGuid();

        var noTasks = await host.UserAsync(host.TenantA, [CalendarPermissions.ViewMeetings, CalendarPermissions.EditMeetings, CalendarPermissions.AccessAllMeetingPeople]);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(noTasks, HttpMethod.Post, $"{Meetings}/{id}/actions/0/convert-to-task")).StatusCode);

        var stranger = await host.UserAsync(host.TenantA, Permissions);
        Assert.NotEqual(HttpStatusCode.OK, (await host.SendAsync(stranger, HttpMethod.Post, $"{Meetings}/{id}/actions/0/convert-to-task")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(organizer, HttpMethod.Post, $"{Meetings}/{id}/actions/0/convert-to-task")).StatusCode);
    }
}
