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

        group.MapPost("/", async (CreateWorkCalendarRequest request, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateWorkCalendarRequest request, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.Update);

        group.MapPost("/{id:guid}/exceptions", async (Guid id, AddWorkCalendarExceptionRequest request, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.AddExceptionAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.Update);

        group.MapDelete("/{id:guid}/exceptions/{exceptionId:guid}", async (Guid id, Guid exceptionId, IWorkCalendarService service, CancellationToken cancellationToken) =>
                (await service.RemoveExceptionAsync(id, exceptionId, cancellationToken)).ToApiResult())
            .RequireAuthorization(CalendarPermissions.Update);

        return app;
    }
}
