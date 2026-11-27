using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.Calendar.Application;
using Nexus.Calendar.Application.Dtos;
using Nexus.Calendar.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Calendar.Endpoints;

public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calendar/work-calendars").WithTags("Calendar").RequireAuthorization();

        // Which days take new tasks and meetings, for any signed-in user (the date pickers and forms check before
        // sending): the default work calendar's non-working days, or usesWorkCalendar=false when there is none (the
        // client then applies the fixed Iranian rule, as the server does). Same source as ISchedulingDayPolicy.
        app.MapGet("/api/calendar/scheduling-days", async (DateOnly from, DateOnly to, ICurrentUserContext currentUser,
                IWorkCalendarService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                if (to < from || to.DayNumber - from.DayNumber > 1000)
                {
                    return Results.BadRequest(new { error = "The range must be at most 1,000 days." });
                }

                var list = await service.ListAsync(currentUser.TenantId.Value, cancellationToken);
                var calendar = list.IsSuccess ? list.Value!.FirstOrDefault(c => c.IsDefault) : null;
                if (calendar is null)
                {
                    return Results.Ok(new SchedulingDaysDto(false, []));
                }

                var days = await service.GetDaysAsync(calendar.Id, from, to, cancellationToken);
                if (days.IsFailure)
                {
                    return Results.Ok(new SchedulingDaysDto(false, []));
                }

                return Results.Ok(new SchedulingDaysDto(true, days.Value!
                    .Where(d => !d.IsWorkingDay)
                    .Select(d => new SchedulingOffDayDto(d.Date, d.Source, d.Reason))
                    .ToList()));
            })
            .WithTags("Calendar")
            .WithSummary("Days that take no new tasks or meetings (the default work calendar's days off) - for any signed-in user")
            .RequireAuthorization();

        group.MapGet("/", async (ICurrentUserContext currentUser, IWorkCalendarService service, CancellationToken cancellationToken) =>
            {
                if (currentUser.TenantId is null)
                {
                    return Results.Unauthorized();
                }

                return (await service.ListAsync(currentUser.TenantId.Value, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization(CalendarPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.View);

        group.MapGet("/{id:guid}/is-working-day", async (Guid id, DateOnly date, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.IsWorkingDayAsync(id, date, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.View);

        group.MapGet("/{id:guid}/days", async (Guid id, DateOnly from, DateOnly to, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.GetDaysAsync(id, from, to, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.View);

        group.MapGet("/official-holidays", async (DateOnly from, DateOnly to, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.ListOfficialHolidaysAsync(from, to, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.View);

        group.MapPost("/", async (CreateWorkCalendarRequest request, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateWorkCalendarRequest request, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.Update);

        group.MapDelete("/{id:guid}", async (Guid id, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.Delete);

        group.MapPost("/{id:guid}/exceptions", async (Guid id, AddWorkCalendarExceptionRequest request, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.AddExceptionAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.Update);

        group.MapDelete("/{id:guid}/exceptions/{exceptionId:guid}", async (Guid id, Guid exceptionId, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.RemoveExceptionAsync(id, exceptionId, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.Update);

        return app;
    }
}
