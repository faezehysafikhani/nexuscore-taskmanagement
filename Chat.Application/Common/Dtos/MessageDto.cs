using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chat.Application.Common.Dtos;

public sealed record MessageDto(
    Guid Id,
    Guid SenderUserId,
    string Text,
    DateTime SentAt,
    bool IsOwnMessage,
    bool IsRead
);
