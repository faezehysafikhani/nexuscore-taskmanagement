using NexusCore.Application.Identity.Dtos;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Sso;

public sealed record SsoProviderDto(string Key, string DisplayName, SsoProviderType Type);

/// <summary>Where to send the browser, and (OIDC) the random value that must come back in the cookie.</summary>
public sealed record SsoStartResult(string RedirectUrl, string? Binder);

public sealed record SsoCompleted(AuthResponse Session, string ReturnUrl);

public interface ISsoService
{
    IReadOnlyList<SsoProviderDto> Providers();

    Task<Result<SsoStartResult>> StartAsync(string key, string? returnUrl, CancellationToken cancellationToken);

    /// <summary>OpenID Connect: the browser came back with a code. <paramref name="binder"/> is the cookie set at the start.</summary>
    Task<Result<SsoCompleted>> CompleteOidcAsync(string key, string? code, string? state, string? error, string? binder, CancellationToken cancellationToken);

    /// <summary>SAML: the provider posted its answer.</summary>
    Task<Result<SsoCompleted>> CompleteSamlAsync(string key, string? samlResponse, string? relayState, CancellationToken cancellationToken);

    Result<string> SamlMetadata(string key);

    /// <summary>The address the browser is sent to after a failed sign-in, with the reason attached.</summary>
    string FailureUrl(string? returnUrl, string message);
}
