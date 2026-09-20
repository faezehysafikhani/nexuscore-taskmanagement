namespace Ticketing.Application.Common.Dtos;

public record TicketDetailsDto(
    Guid Id,
    string Number,
    string Title,
    string Description,
    string Status,
    string Priority,
    Guid? CreatedByUserId,
    Guid? AssignedToUserId,
    DateTimeOffset CreatedAt,
    List<TicketCommentDto> Comments
);

public record TicketCommentDto(
    Guid Id,
    Guid? UserId,
    string Text,
    DateTimeOffset CreatedAt
);