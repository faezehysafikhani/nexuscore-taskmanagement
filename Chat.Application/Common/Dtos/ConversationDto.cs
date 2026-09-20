using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chat.Application.Common.Dtos;

public sealed record ConversationDto(
    Guid Id,
    string? Title,
    string Type,
    DateTime CreatedAt,
    int ParticipantCount,
    string? LastMessage,
    DateTime? LastMessageAt
);
