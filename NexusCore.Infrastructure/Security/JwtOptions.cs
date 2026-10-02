namespace NexusCore.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "NexusCore";
    public string Audience { get; set; } = "NexusCore";
    public string SigningKey { get; set; } = "change-me-to-a-strong-production-secret-at-least-32-characters";
    public int AccessTokenMinutes { get; set; } = 30;

    // Placeholder values shipped as defaults/examples in source - never acceptable as the real
    // production signing key.
    private static readonly string[] KnownPlaceholders =
    [
        "change-me-to-a-strong-production-secret-at-least-32-characters",
        "replace-this-development-secret-with-a-production-secret-32chars",
    ];

    /// <summary>
    /// A production host must not start with a missing, placeholder, or too-short signing key -
    /// that would let anyone forge a valid access token. Throws instead of starting; the message
    /// never includes the key's value.
    /// </summary>
    public void EnsureSafeForProduction()
    {
        if (string.IsNullOrWhiteSpace(SigningKey)
            || KnownPlaceholders.Contains(SigningKey)
            || SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is missing, a development placeholder, or shorter than 32 characters. " +
                "Set a real secret via the Jwt__SigningKey environment variable (or another production " +
                "configuration source) before starting this service in a production environment.");
        }
    }
}
