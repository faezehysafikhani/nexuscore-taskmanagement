using Chat.Application.Conversations.Commands.CreateDirectConversation;
using Chat.Application.Conversations.Commands.CreateGroupConversation;
using Chat.Application.Conversations.Queries.GetConversationMessages;
using Chat.Application.Conversations.Queries.GetMyConversations;
using Chat.Application.Conversations.Queries.GetUnreadCount;
using MediatR;
using NexusCore.Application.Common;

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

            return result.ToApiResult();
        });

        group.MapGet("/{conversationId:Guid}/messages", async (
            Guid conversationId,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var safePage = page is > 0 ? page.Value : 1;
            var safePageSize = pageSize is > 0 ? Math.Min(pageSize.Value, 500) : 50;

            var result = await sender.Send(
                new GetConversationMessagesQuery(conversationId, safePage, safePageSize),
                cancellationToken);

            return result.ToApiResult();
        });

        group.MapPost("/direct", async (
            CreateDirectConversationCommand command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command, cancellationToken);

            return result.ToApiResult();
        });

        group.MapPost("/group", async (
            CreateGroupConversationCommand command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command, cancellationToken);

            return result.ToApiResult();
        });

        group.MapGet("/unread-count", async (
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new GetUnreadCountQuery(),
                cancellationToken);

            return result.ToApiResult();
        });

        return app;
    }
}
