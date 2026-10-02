using Chat.Api.Hubs;
using Chat.Api.Hubs.Contracts;
using Chat.Application.Direct;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NexusCore.Application.Common;
using NexusCore.Application.Security.RateLimiting;

namespace Chat.Api.Endpoints;

/// <summary>
/// Person-to-person chat addressed by the other user's id. Built on the same conversations and
/// messages as the rest of the chat module: each pair of users has one direct conversation,
/// created on the first message.
/// </summary>
public static class DirectMessageEndpoints
{
    public static IEndpointRouteBuilder MapDirectMessageEndpoints(this IEndpointRouteBuilder app)
    {
        var direct = app.MapGroup("/api/chat/direct")
            .WithTags("Chat - Direct messages")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi);

        direct.MapGet("/unread-counts", async (ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new GetUnreadCountsBySenderQuery(), cancellationToken)).ToApiResult())
            .WithSummary("Unread messages per sender");

        direct.MapGet("/directory", async (ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new GetChatDirectoryQuery(), cancellationToken)).ToApiResult())
            .WithSummary("Every active user of your tenant you may start a direct chat with");

        direct.MapGet("/{userId:guid}/messages", async (
                Guid userId,
                int? page,
                int? pageSize,
                ISender sender,
                CancellationToken cancellationToken) =>
            (await sender.Send(new GetDirectMessagesQuery(userId, page ?? 1, pageSize ?? 500), cancellationToken)).ToApiResult())
            .WithSummary("Messages between you and the user, oldest first");

        // multipart/form-data: "text" and an optional "file".
        direct.MapPost("/{userId:guid}/messages", async (
                Guid userId,
                [FromForm] string? text,
                IFormFile? file,
                ISender sender,
                IHubContext<ChatHub> hub,
                CancellationToken cancellationToken) =>
            {
                await using var content = file?.OpenReadStream();
                var attachment = file is null
                    ? null
                    : new ChatAttachmentUpload(file.FileName, file.ContentType, file.Length, content!);

                var result = await sender.Send(new SendDirectMessageCommand(userId, text, attachment), cancellationToken);
                if (result.IsSuccess && result.Value is { } message)
                {
                    await hub.Clients
                        .Group($"conversation:{message.ConversationId}")
                        .SendAsync(
                            "MessageReceived",
                            new MessageReceivedDto(message.Id, message.ConversationId, message.SenderUserId, message.Text, message.SentAtUtc.UtcDateTime),
                            cancellationToken);
                }

                return result.ToApiResult();
            })
            .DisableAntiforgery()
            .RequireRateLimiting(NexusRateLimitPolicies.ChatSend)
            .WithSummary("Send a message (text and/or one attachment) to the user");

        direct.MapPost("/{userId:guid}/read", async (Guid userId, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new MarkDirectConversationReadCommand(userId), cancellationToken)).ToApiResult())
            .WithSummary("Mark everything the user sent you as read");

        return app;
    }
}
