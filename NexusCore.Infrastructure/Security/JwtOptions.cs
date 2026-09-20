namespace NexusCore.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "NexusCore";
    public string Audience { get; set; } = "NexusCore";
    public string SigningKey { get; set; } = "change-me-to-a-strong-production-secret-at-least-32-characters";
    public int AccessTokenMinutes { get; set; } = 30;
}
