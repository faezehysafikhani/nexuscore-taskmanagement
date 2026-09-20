using Chat.Api.Hubs;
using Chat.Api.Hubs.Contracts;
using Chat.Application.Messages.Commands.SendMessage;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace Chat.Api.Endpoints;

public static class MessageEndpoints
{
    public static void MapMessageEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/chat/messages",
            async (
                SendMessageCommand command,
                ISender sender,
                IHubContext<ChatHub> hub,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    command,
                    cancellationToken);

                if (result.IsFailure)
                    return Results.BadRequest(result);

                var messageId = result.Value;

                await hub.Clients
    .Group($"conversation:{command.ConversationId}")
    .SendAsync(
        "MessageReceived",
        new MessageReceivedDto(
            messageId,
            command.ConversationId,
            command.SenderUserId,
            command.Text,
            DateTime.UtcNow),
        cancellationToken);

                return Results.Ok(result);
            });
    }
}