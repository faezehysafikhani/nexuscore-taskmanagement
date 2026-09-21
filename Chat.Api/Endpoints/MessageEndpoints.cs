using Chat.Api.Hubs;
using Chat.Api.Hubs.Contracts;
using Chat.Application.Messages.Commands.DeleteMessage;
using Chat.Application.Messages.Commands.EditMessage;
using Chat.Application.Messages.Commands.MarkAsRead;
using Chat.Application.Messages.Commands.SendMessage;
using Chat.Application.Direct;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Chat.Api.Endpoints;

public sealed record EditMessageRequest(string Text);

public static class MessageEndpoints
{
    public static void MapMessageEndpoints(
        this IEndpointRouteBuilder app)
    {
        var messages = app.MapGroup("/api/chat/messages")
            .WithTags("Chat - Messages")
            .RequireAuthorization();

        messages.MapPost(
            "/",
            async (
                SendMessageCommand command,
                ISender sender,
                ICurrentUserContext currentUser,
                IHubContext<ChatHub> hub,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    command,
                    cancellationToken);

                if (result.IsSuccess)
                {
                    // The sender announced to the room is the signed-in user - never a value
                    // taken from the request body.
                    await hub.Clients
                        .Group($"conversation:{command.ConversationId}")
                        .SendAsync(
                            "MessageReceived",
                            new MessageReceivedDto(
                                result.Value,
                                command.ConversationId,
                                currentUser.UserId,
                                command.Text,
                                DateTime.UtcNow),
                            cancellationToken);
                }

                return result.ToApiResult();
            });

        messages.MapPut("/{messageId:guid}", async (
                Guid messageId,
                EditMessageRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            (await sender.Send(new EditMessageCommand(messageId, request.Text), cancellationToken)).ToApiResult())
            .WithSummary("Edit your own message");

        messages.MapDelete("/{messageId:guid}", async (
                Guid messageId,
                ISender sender,
                CancellationToken cancellationToken) =>
            (await sender.Send(new DeleteMessageCommand(messageId), cancellationToken)).ToApiResult())
            .WithSummary("Delete your own message");

        messages.MapPost("/{messageId:guid}/read", async (
                Guid messageId,
                ISender sender,
                CancellationToken cancellationToken) =>
            (await sender.Send(new MarkAsReadCommand(messageId), cancellationToken)).ToApiResult());

        messages.MapGet("/{messageId:guid}/attachment", async (
                Guid messageId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMessageAttachmentQuery(messageId), cancellationToken);
                if (result.IsFailure || result.Value is not { } download)
                {
                    return result.ToApiResult();
                }

                return Results.File(download.Content, download.ContentType, download.FileName);
            })
            .WithSummary("Download a message attachment (participants only)");
    }
}
