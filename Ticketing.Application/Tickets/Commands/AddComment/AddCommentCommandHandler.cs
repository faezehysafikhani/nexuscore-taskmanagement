using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Tickets.Commands.AddComment;

public class AddCommentCommandHandler
    : IRequestHandler<AddCommentCommand, Result<Guid>>
{
    private readonly ITicketingDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public AddCommentCommandHandler(
        ITicketingDbContext db,
        ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(
        AddCommentCommand request,
        CancellationToken cancellationToken)
    {
        var exists = await _db.Tickets
            .AnyAsync(x => x.Id == request.TicketId, cancellationToken);

        if (!exists)
            return Result.Failure<Guid>(new Error("tickets.not_found", "تیکت یافت نشد"));

        var comment = new TicketComment(
            Guid.NewGuid(),
            request.TicketId,
            _currentUser.UserId,
            request.Text);

        _db.TicketComments.Add(comment);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(comment.Id);
    }
}