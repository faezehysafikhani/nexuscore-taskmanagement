using Chat.Application.Abstractions;
using Chat.Application.Teams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Services;
using NexusCore.SharedKernel.Interfaces;

namespace Chat.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private const string PresenceUserKey = "presence-user";

    private readonly IChatDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IUserPresenceTracker _presence;
    private readonly TeamConversationService _teams;

    public ChatHub(
        IChatDbContext db,
        ICurrentUserContext currentUser,
        IUserPresenceTracker presence,
        TeamConversationService teams)
    {
        _db = db;
        _currentUser = currentUser;
        _presence = presence;
        _teams = teams;
    }

    /// <summary>An open chat connection counts toward its user's presence, like any other hub connection.</summary>
    public override async Task OnConnectedAsync()
    {
        if (_currentUser.UserId is { } userId)
        {
            Context.Items[PresenceUserKey] = userId;
            _presence.Connected(userId, Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(PresenceUserKey, out var value) && value is Guid userId)
        {
            _presence.Disconnected(userId, Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    public async Task JoinConversation(Guid conversationId)
    {
        var isParticipant = await _db.ConversationParticipants
            .AnyAsync(p =>
                p.ConversationId == conversationId &&
                p.UserId == _currentUser.UserId);

        // A team's thread is only for its current members, whatever rows are left from before.
        if (!isParticipant
            || _currentUser.UserId is not { } userId
            || !await _teams.IsCurrentMemberAsync(conversationId, userId, Context.ConnectionAborted))
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