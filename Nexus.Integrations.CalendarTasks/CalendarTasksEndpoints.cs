using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NexusCore.Application.Common;

namespace Nexus.Integrations.CalendarTasks;

public static class CalendarTasksEndpoints
{
    /// <summary>The routes of the meetings-to-tasks bridge. Call after the meeting endpoints.</summary>
    public static IEndpointRouteBuilder MapCalendarTasksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calendar/meetings").WithTags("Meetings - tasks").RequireAuthorization();

        group.MapPost("/{id:guid}/actions/{index:int}/convert-to-task", async (Guid id, int index, MeetingActionConverter converter, CancellationToken ct) =>
                (await converter.ConvertAsync(id, index, ct)).ToApiResult())
            .WithSummary("Turn one action item of the minutes into a task (the task keeps the meeting as its source)");

        group.MapPost("/{id:guid}/actions/convert-all", async (Guid id, MeetingActionConverter converter, CancellationToken ct) =>
                (await converter.ConvertAsync(id, null, ct)).ToApiResult())
            .WithSummary("Turn every action item that is not a task yet into a task");

        return app;
    }
}
