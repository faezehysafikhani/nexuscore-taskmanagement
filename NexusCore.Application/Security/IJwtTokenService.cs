using NexusCore.Application.Identity.Dtos;
using NexusCore.Domain.Identity;

namespace NexusCore.Application.Security;

public interface IJwtTokenService
{
    /// <summary>An access token for one session: <paramref name="sessionId"/> becomes its <c>sid</c> claim, which every request checks.</summary>
    AuthToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions, Guid? sessionId = null);
    string CreateRefreshToken();
}

public sealed record AuthToken(string Token, DateTimeOffset ExpiresAtUtc);
