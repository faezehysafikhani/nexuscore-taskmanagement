using Chat.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;

namespace Chat.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public ChatHub(
        IChatDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task JoinConversation(Guid conversationId)
    {
        var isParticipant = await _db.ConversationParticipants
            .AnyAsync(p =>
                p.ConversationId == conversationId &&
                p.UserId == _currentUser.UserId);

        if (!isParticipant)
        {
            throw new HubException(
                "You are not a participant of this conversation.");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            GetConversationGroup(conversationId));
    }

    public async Task LeaveConversation(Guid conversationId)
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            GetConversationGroup(conversationId));
    }

    private static string GetConversationGroup(Guid conversationId)
    {
        return $"conversation:{conversationId}";
    }
}