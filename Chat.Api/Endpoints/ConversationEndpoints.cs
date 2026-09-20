using Chat.Application.Conversations.Commands.CreateDirectConversation;
using Chat.Application.Conversations.Commands.CreateGroupConversation;
using Chat.Application.Conversations.Queries.GetConversationMessages;
using Chat.Application.Conversations.Queries.GetMyConversations;
using Chat.Application.Conversations.Queries.GetUnreadCount;
using MediatR;
using Microsoft.AspNetCore.Hosting.Server;

namespace Chat.Api.Endpoints;

public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat/conversations")
            .WithTags("Chat - Conversations")
            .RequireAuthorization();

        group.MapGet("/", async (
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new GetMyConversationsQuery(),
                cancellationToken);

            return Results.Ok(result);
        });

        group.MapGet("/{conversationId:Guid}/messages", async (
            Guid conversationId,
            int page,
            int pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 50 : pageSize;

            var result = await sender.Send(
                new GetConversationMessagesQuery(conversationId, page, pageSize),
                cancellationToken);

            return Results.Ok(result);
        });

        group.MapPost("/direct", async (
            CreateDirectConversationCommand command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command, cancellationToken);

            return Results.Ok(result);
        });

        group.MapPost("/group", async (
            CreateGroupConversationCommand command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command, cancellationToken);

            return Results.Ok(result);
        });
        group.MapGet("/unread-count", async (
    ISender sender,
    CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new GetUnreadCountQuery(),
                cancellationToken);

            return Results.Ok(result);
        });
        return app;
    }
}