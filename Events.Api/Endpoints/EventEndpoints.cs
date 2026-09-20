using Events.Application.Commands.CreateEvent;
using Events.Application.Commands.DeleteEvent;
using Events.Application.Commands.UpdateEvent;
using Events.Application.DTOs;
using Events.Application.Queries.GetEventById;
using Events.Application.Queries.GetMyEvents;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Events.Api.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events")
            .WithTags("Events")
            .RequireAuthorization();

        // Create Event
        group.MapPost("/", async (
            CreateEventRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateEventCommand(
                request.Title,
                request.Description,
                request.StartAtUtc,
                request.EndAtUtc,
                request.ReminderMinutesBefore);

            var result = await sender.Send(command, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/events/{result.Value!.Id}", result.Value)
                : Results.BadRequest(result.Error);
        })
        .WithName("CreateEvent")
        .WithSummary("Creates a new personal event for the authenticated user.");

        // Get My Events
        group.MapGet("/", async (
            DateTime? startDate,
            DateTime? endDate,
            bool? isCompleted,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var query = new GetMyEventsQuery(startDate, endDate, isCompleted);
            var result = await sender.Send(query, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(result.Error);
        })
        .WithName("GetMyEvents")
        .WithSummary("Retrieves all personal events for the authenticated user within an optional date range.");

        // Get Event by Id
        group.MapGet("/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var query = new GetEventByIdQuery(id);
            var result = await sender.Send(query, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : (result.Error.Code == "not_found" ? Results.NotFound(result.Error) : Results.BadRequest(result.Error));
        })
        .WithName("GetEventById")
        .WithSummary("Retrieves a personal event by its ID.");

        // Update Event
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateEventRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateEventCommand(
                id,
                request.Title,
                request.Description,
                request.StartAtUtc,
                request.EndAtUtc,
                request.IsCompleted,
                request.ReminderMinutesBefore);

            var result = await sender.Send(command, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : (result.Error.Code == "not_found" ? Results.NotFound(result.Error) : Results.BadRequest(result.Error));
        })
        .WithName("UpdateEvent")
        .WithSummary("Updates a personal event.");

        // Delete Event
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var command = new DeleteEventCommand(id);
            var result = await sender.Send(command, cancellationToken);
            return result.IsSuccess
                ? Results.NoContent()
                : (result.Error.Code == "not_found" ? Results.NotFound(result.Error) : Results.BadRequest(result.Error));
        })
        .WithName("DeleteEvent")
        .WithSummary("Deletes a personal event.");
    }
}
