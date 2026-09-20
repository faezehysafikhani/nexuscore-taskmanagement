namespace Ticketing.Application.Common.Dtos;

public record TicketDto(
    Guid Id,
    string Number,
    string Title,
    string Status,
    string Priority,
    Guid? AssignedToUserId,
    DateTimeOffset CreatedAt
);