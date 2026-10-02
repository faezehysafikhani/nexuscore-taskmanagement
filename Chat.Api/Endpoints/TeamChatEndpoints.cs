using Chat.Api.Hubs;
using Chat.Api.Hubs.Contracts;
using Chat.Application.Direct;
using Chat.Application.Teams;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NexusCore.Application.Common;
using NexusCore.Application.Security.RateLimiting;

namespace Chat.Api.Endpoints;

/// <summary>
/// A team's shared conversation, addressed by the team's id: one thread for all of the team's
/// active members, each of whom reads it and writes to it. Only members of the (active) team
/// reach it; everyone else gets "not found".
/// </summary>
public static class TeamChatEndpoints
{
    public static IEndpointRouteBuilder MapTeamChatEndpoints(this IEndpointRouteBuilder app)
    {
        var teams = app.MapGroup("/api/chat/teams")
            .WithTags("Chat - Team conversations")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi);

        teams.MapGet("/unread-counts", async (ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new GetTeamUnreadCountsQuery(), cancellationToken)).ToApiResult())
            .WithSummary("Unread messages per team conversation");

        teams.MapGet("/{teamId:guid}/messages", async (Guid teamId, int? page, int? pageSize, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new GetTeamMessagesQuery(teamId, page ?? 1, pageSize ?? 500), cancellationToken)).ToApiResult())
            .WithSummary("The team's messages, oldest first");

        // multipart/form-data: "text" and an optional "file".
        teams.MapPost("/{teamId:guid}/messages", async (
                Guid teamId,
                [FromForm] string? text,
                IFormFile? file,
                ISender sender,
                IHubContext<ChatHub> hub,
                CancellationToken cancellationToken) =>
            {
                await using var content = file?.OpenReadStream();
                var attachment = file is null ? null : new ChatAttachmentUpload(file.FileName, file.ContentType, file.Length, content!);

                var result = await sender.Send(new SendTeamMessageCommand(teamId, text, attachment), cancellationToken);
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
            .WithSummary("Send a message (text and/or one attachment) to the team");

        teams.MapPost("/{teamId:guid}/read", async (Guid teamId, ISender sender, CancellationToken cancellationToken) =>
                (await sender.Send(new MarkTeamConversationReadCommand(teamId), cancellationToken)).ToApiResult())
            .WithSummary("Mark the team's messages as read");

        return app;
    }
}
