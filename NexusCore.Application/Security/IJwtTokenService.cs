using NexusCore.Application.Identity.Dtos;
using NexusCore.Domain.Identity;

namespace NexusCore.Application.Security;

public interface IJwtTokenService
{
    AuthToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions);
    string CreateRefreshToken();
}

public sealed record AuthToken(string Token, DateTimeOffset ExpiresAtUtc);
