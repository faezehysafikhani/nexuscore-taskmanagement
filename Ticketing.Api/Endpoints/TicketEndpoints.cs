using MediatR;
using Microsoft.AspNetCore.Hosting.Server;
using NexusCore.Application.Common;
using Ticketing.Application.Tickets.Commands.AddComment;
using Ticketing.Application.Tickets.Commands.AssignTicket;
using Ticketing.Application.Tickets.Commands.ChangePriority;
using Ticketing.Application.Tickets.Commands.ChangeStatus;
using Ticketing.Application.Tickets.Commands.CreateTicket;
using Ticketing.Application.Tickets.Queries.GetMyTickets;
using Ticketing.Application.Tickets.Queries.GetTicketDetails;
using Ticketing.Domain.Enums;

namespace Ticketing.Api.Endpoints;

public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tickets").RequireAuthorization().WithTags("Ticketing");

        group.MapGet("/my", async (ISender sender, CancellationToken ct) =>
        {
            return (await sender.Send(new GetMyTicketsQuery(), ct)).ToApiResult();
        });

        group.MapPost("/", async (CreateTicketCommand command, ISender sender, CancellationToken ct) =>
        {
            return (await sender.Send(command, ct)).ToApiResult();
        });

        group.MapPost("/{ticketId:guid}/comments", async (
            Guid ticketId,
            AddCommentRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            return (await sender.Send(
                new AddCommentCommand(ticketId, request.Text), ct)).ToApiResult();
        });

        group.MapPut("/{ticketId:guid}/assign", async (
            Guid ticketId,
            AssignTicketRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            return (await sender.Send(
                new AssignTicketCommand(ticketId, request.UserId), ct)).ToApiResult();
        });
        group.MapGet("/{ticketId:guid}", async (
    Guid ticketId,
    ISender sender,
    CancellationToken ct) =>
        {
            return (await sender.Send(
                new GetTicketDetailsQuery(ticketId), ct)).ToApiResult();
        });

        group.MapPut("/{ticketId:guid}/status", async (
            Guid ticketId,
            ChangeStatusRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            return (await sender.Send(
                new ChangeStatusCommand(ticketId, request.Status), ct)).ToApiResult();
        });

        group.MapPut("/{ticketId:guid}/priority", async (
            Guid ticketId,
            ChangePriorityRequest request,
            ISender sender,
            CancellationToken ct) =>
        {
            return (await sender.Send(
                new ChangePriorityCommand(ticketId, request.Priority), ct)).ToApiResult();
        });

        return app;
    }

    public record ChangeStatusRequest(TicketStatus Status);
    public record ChangePriorityRequest(TicketPriority Priority);
    public record AddCommentRequest(string Text);
    public record AssignTicketRequest(Guid UserId);
}