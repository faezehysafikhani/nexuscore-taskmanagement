using Chat.Application.Direct;
using MediatR;
using NexusCore.SharedKernel.Results;

namespace Chat.Application.Conversations.Commands.CreateDirectConversation;

/// <summary>
/// Returns the conversation between the caller and the other user, creating it on first use.
/// A pair of users has exactly one direct conversation.
/// </summary>
public sealed class CreateDirectConversationCommandHandler
    : IRequestHandler<CreateDirectConversationCommand, Result<Guid>>
{
    private readonly DirectConversationService _conversations;

    public CreateDirectConversationCommandHandler(DirectConversationService conversations)
    {
        _conversations = conversations;
    }

    public async Task<Result<Guid>> Handle(
        CreateDirectConversationCommand request,
        CancellationToken cancellationToken)
    {
        var pair = await _conversations.ResolvePairAsync(request.OtherUserId, cancellationToken);
        if (pair.IsFailure)
            return Result.Failure<Guid>(pair.Error);

        var (me, other) = pair.Value;
        var conversation = await _conversations.FindOrCreateAsync(me.Id, other.Id, cancellationToken);

        return Result.Success(conversation.Id);
    }
}
