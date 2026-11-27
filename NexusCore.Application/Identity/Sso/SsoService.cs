using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Services;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Sso;

/// <summary>
/// Single sign-on with OpenID Connect and SAML 2.0 (CAT-226). The identity provider proves who the person is; the person
/// must already exist here (matched by username, e-mail or mobile number). Nothing about the sign-in is trusted until the
/// provider's signature has been checked: the OIDC id_token against the provider's published keys, the SAML answer
/// against the certificate set in the configuration.
/// </summary>
public sealed class SsoService(
    IOptions<SsoOptions> options,
    IIdentityRepository repository,
    AuthSessionIssuer sessionIssuer,
    IPlatformService platformService,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpClientFactory,
    ILogger<SsoService> logger) : ISsoService
{
    private const string Purpose = "NexusCore.Sso.State.v1";
    private const string SamlProtocolNs = "urn:oasis:names:tc:SAML:2.0:protocol";
    private const string SamlAssertionNs = "urn:oasis:names:tc:SAML:2.0:assertion";
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);
    private const string Unavailable = "ورود یکپارچه در دسترس نیست.";
    private const string Failed = "ورود یکپارچه انجام نشد؛ دوباره تلاش کنید یا با نام کاربری و رمز وارد شوید.";

    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> Discovery = new();
    // Assertion ids seen, until they would expire anyway: an answer cannot be replayed.
    private static readonly ConcurrentDictionary<string, DateTimeOffset> SeenAssertions = new();

    private sealed record State(string Key, string Nonce, string Verifier, string ReturnUrl, string RequestId, string? Binder, DateTimeOffset ExpiresAtUtc);

    private SsoOptions Options => options.Value;

    private SsoProviderOptions? Find(string key, SsoProviderType? type = null) =>
        Options.Providers.FirstOrDefault(p => p.Enabled && string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase) && (type is null || p.Type == type));

    public IReadOnlyList<SsoProviderDto> Providers() =>
        Options.Providers.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key)).Select(p => new SsoProviderDto(p.Key, string.IsNullOrWhiteSpace(p.DisplayName) ? p.Key : p.DisplayName, p.Type)).ToList();

    // --- return address and state ---

    private string SafeReturnUrl(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && Uri.TryCreate(requested, UriKind.RelativeOrAbsolute, out var uri))
        {
            // A relative path of this site ("/tasks") is fine; "//host" and "/\host" are not paths.
            if (!uri.IsAbsoluteUri && requested.StartsWith('/') && !requested.StartsWith("//", StringComparison.Ordinal) && !requested.StartsWith("/\\", StringComparison.Ordinal))
            {
                return requested;
            }

            if (uri.IsAbsoluteUri && Options.AllowedReturnUrls.Any(a => requested.StartsWith(a.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) || string.Equals(requested, a.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            {
                return requested;
            }
        }

        return Options.DefaultReturnUrl;
    }

    public string FailureUrl(string? returnUrl, string message)
    {
        var target = SafeReturnUrl(returnUrl);
        return $"{target}{(target.Contains('?') ? '&' : '?')}sso_error={Uri.EscapeDataString(message)}";
    }

    private string Protect(State state) =>
        Base64Url(dataProtection.CreateProtector(Purpose).Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state))));

    private State? Unprotect(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<State>(Encoding.UTF8.GetString(dataProtection.CreateProtector(Purpose).Unprotect(FromBase64Url(value))));
            return state is not null && state.ExpiresAtUtc > DateTimeOffset.UtcNow && string.Equals(state.Key, key, StringComparison.OrdinalIgnoreCase) ? state : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    private static string RandomToken(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    // --- start ---

    public async Task<Result<SsoStartResult>> StartAsync(string key, string? returnUrl, CancellationToken cancellationToken)
    {
        var provider = Find(key);
        if (provider is null) return Result.Failure<SsoStartResult>(Error.NotFound(Unavailable));
        var safeReturn = SafeReturnUrl(returnUrl);
        var expires = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(Options.StateMinutes, 2, 60));

        try
        {
            return provider.Type == SsoProviderType.Oidc
                ? await StartOidcAsync(provider, safeReturn, expires, cancellationToken)
                : StartSaml(provider, safeReturn, expires);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "SSO start failed for {Provider}.", provider.Key);
            return Result.Failure<SsoStartResult>(Error.Validation(Unavailable));
        }
    }

    private async Task<Result<SsoStartResult>> StartOidcAsync(SsoProviderOptions p, string returnUrl, DateTimeOffset expires, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.Authority) || string.IsNullOrWhiteSpace(p.ClientId) || string.IsNullOrWhiteSpace(p.RedirectUri))
            return Result.Failure<SsoStartResult>(Error.Validation(Unavailable));

        var config = await ConfigurationOf(p).GetConfigurationAsync(ct);
        var verifier = RandomToken(48);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var binder = RandomToken(24);
        var state = new State(p.Key, RandomToken(24), verifier, returnUrl, string.Empty, binder, expires);

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = p.ClientId,
            ["redirect_uri"] = p.RedirectUri,
            ["scope"] = p.Scopes,
            ["state"] = Protect(state),
            ["nonce"] = state.Nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        var url = config.AuthorizationEndpoint + (config.AuthorizationEndpoint.Contains('?') ? "&" : "?")
            + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return Result.Success(new SsoStartResult(url, binder));
    }

    private Result<SsoStartResult> StartSaml(SsoProviderOptions p, string returnUrl, DateTimeOffset expires)
    {
        if (string.IsNullOrWhiteSpace(p.IdpSsoUrl) || string.IsNullOrWhiteSpace(p.SpEntityId) || string.IsNullOrWhiteSpace(p.AcsUrl) || string.IsNullOrWhiteSpace(p.IdpCertificate))
            return Result.Failure<SsoStartResult>(Error.Validation(Unavailable));

        var requestId = "_" + Guid.NewGuid().ToString("N");
        var xml = $"""<samlp:AuthnRequest xmlns:samlp="{SamlProtocolNs}" xmlns:saml="{SamlAssertionNs}" ID="{requestId}" Version="2.0" IssueInstant="{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}" Destination="{Xml(p.IdpSsoUrl)}" ProtocolBinding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST" AssertionConsumerServiceURL="{Xml(p.AcsUrl)}"><saml:Issuer>{Xml(p.SpEntityId)}</saml:Issuer><samlp:NameIDPolicy AllowCreate="false"/></samlp:AuthnRequest>""";
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(Encoding.UTF8.GetBytes(xml));
        }

        var state = new State(p.Key, string.Empty, string.Empty, returnUrl, requestId, null, expires);
        var url = p.IdpSsoUrl + (p.IdpSsoUrl.Contains('?') ? "&" : "?")
            + "SAMLRequest=" + Uri.EscapeDataString(Convert.ToBase64String(output.ToArray()))
            + "&RelayState=" + Uri.EscapeDataString(Protect(state));
        return Result.Success(new SsoStartResult(url, null));
    }

    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private ConfigurationManager<OpenIdConnectConfiguration> ConfigurationOf(SsoProviderOptions p) =>
        Discovery.GetOrAdd(p.Key, _ => new ConfigurationManager<OpenIdConnectConfiguration>(
            p.Authority!.TrimEnd('/') + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(httpClientFactory.CreateClient()) { RequireHttps = p.RequireHttpsMetadata }));

    // --- OpenID Connect: the code comes back ---

    public async Task<Result<SsoCompleted>> CompleteOidcAsync(string key, string? code, string? state, string? error, string? binder, CancellationToken cancellationToken)
    {
        var provider = Find(key, SsoProviderType.Oidc);
        if (provider is null) return Result.Failure<SsoCompleted>(Error.NotFound(Unavailable));
        var saved = Unprotect(state, key);
        if (saved is null || string.IsNullOrEmpty(saved.Binder) || !FixedEquals(saved.Binder, binder))
            return Result.Failure<SsoCompleted>(Error.Unauthorized(Failed));
        if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code))
            return Result.Failure<SsoCompleted>(Error.Unauthorized(Failed));

        try
        {
            var config = await ConfigurationOf(provider).GetConfigurationAsync(cancellationToken);
            using var http = httpClientFactory.CreateClient();
            using var response = await http.PostAsync(config.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = provider.RedirectUri ?? string.Empty,
                ["client_id"] = provider.ClientId ?? string.Empty,
                ["client_secret"] = provider.ClientSecret ?? string.Empty,
                ["code_verifier"] = saved.Verifier,
            }), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("SSO token request to {Provider} returned {Status}.", provider.Key, (int)response.StatusCode);
                return Result.Failure<SsoCompleted>(Error.Unauthorized(Failed));
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!json.RootElement.TryGetProperty("id_token", out var idTokenElement) || idTokenElement.GetString() is not { Length: > 0 } idToken)
                return Result.Failure<SsoCompleted>(Error.Unauthorized(Failed));

            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(idToken, new TokenValidationParameters
            {
                ValidIssuer = config.Issuer,
                ValidAudience = provider.ClientId,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = Skew,
            }, out var validated);

            var jwt = (JwtSecurityToken)validated;
            if (!string.Equals(jwt.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value, saved.Nonce, StringComparison.Ordinal))
                return Result.Failure<SsoCompleted>(Error.Unauthorized(Failed));

            var claimName = provider.UserClaim ?? provider.MatchBy switch { SsoMatchBy.Email => "email", SsoMatchBy.Phone => "phone_number", _ => "preferred_username" };
            var value = principal.FindFirst(claimName)?.Value;
            return await SignInAsync(provider, value, saved.ReturnUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is SecurityTokenException or HttpRequestException or InvalidOperationException or JsonException or ArgumentException)
        {
            logger.LogWarning(ex, "SSO sign-in through {Provider} failed.", provider.Key);
            return Result.Failure<SsoCompleted>(Error.Unauthorized(Failed));
        }
    }

    private static bool FixedEquals(string a, string? b) =>
        b is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    // --- SAML: the provider posts its answer ---

    public async Task<Result<SsoCompleted>> CompleteSamlAsync(string key, string? samlResponse, string? relayState, CancellationToken cancellationToken)
    {
        var provider = Find(key, SsoProviderType.Saml);
        if (provider is null) return Result.Failure<SsoCompleted>(Error.NotFound(Unavailable));
        var saved = Unprotect(relayState, key);
        if (saved is null || string.IsNullOrEmpty(saved.RequestId) || string.IsNullOrWhiteSpace(samlResponse))
            return Result.Failure<SsoCompleted>(Error.Unauthorized(Failed));

        try
        {
            var value = ReadVerifiedAssertion(provider, samlResponse, saved.RequestId);
            return value is null
                ? Result.Failure<SsoCompleted>(Error.Unauthorized(Failed))
                : await SignInAsync(provider, value, saved.ReturnUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is XmlException or CryptographicException or FormatException or InvalidOperationException)
        {
            logger.LogWarning(ex, "SAML answer from {Provider} refused.", provider.Key);
            return Result.Failure<SsoCompleted>(Error.Unauthorized(Failed));
        }
    }

    private string? ReadVerifiedAssertion(SsoProviderOptions p, string samlResponse, string requestId)
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var reader = XmlReader.Create(new StringReader(Encoding.UTF8.GetString(Convert.FromBase64String(samlResponse))),
                   new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0 }))
        {
            document.Load(reader);
        }

        var ns = new XmlNamespaceManager(document.NameTable);
        ns.AddNamespace("samlp", SamlProtocolNs);
        ns.AddNamespace("saml", SamlAssertionNs);
        ns.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

        var root = document.DocumentElement;
        if (root is null || root.LocalName != "Response" || root.NamespaceURI != SamlProtocolNs) return null;

        // Success, for us, from the provider that was asked.
        if (root.SelectSingleNode("samlp:Status/samlp:StatusCode/@Value", ns)?.Value != "urn:oasis:names:tc:SAML:2.0:status:Success") return null;
        if (root.GetAttribute("InResponseTo") != requestId) return null;
        if (root.GetAttribute("Destination") is { Length: > 0 } destination && !string.Equals(destination, p.AcsUrl, StringComparison.Ordinal)) return null;

        // One assertion, in the clear. An encrypted one or a second one is how signatures get wrapped.
        if (document.SelectNodes("//saml:Assertion", ns)!.Count != 1 || document.SelectNodes("//saml:EncryptedAssertion", ns)!.Count != 0) return null;
        var assertion = (XmlElement)document.SelectSingleNode("//saml:Assertion", ns)!;
        if (!ReferenceEquals(assertion.ParentNode, root)) return null;

        // The signature that counts is over the response or over the assertion - and what is read below comes from inside the signed element.
        var signedElement = VerifiedSignedElement(p, document, root, assertion, ns);
        if (signedElement is null) return null;

        if (assertion.SelectSingleNode("saml:Issuer", ns)?.InnerText.Trim() != p.IdpEntityId) return null;

        var now = DateTimeOffset.UtcNow;
        var conditions = assertion.SelectSingleNode("saml:Conditions", ns) as XmlElement;
        if (conditions is null) return null;
        if (conditions.GetAttribute("NotBefore") is { Length: > 0 } nb && DateTimeOffset.Parse(nb, null, System.Globalization.DateTimeStyles.AssumeUniversal) > now + Skew) return null;
        if (!conditions.HasAttribute("NotOnOrAfter")) return null;
        var notOnOrAfter = DateTimeOffset.Parse(conditions.GetAttribute("NotOnOrAfter"), null, System.Globalization.DateTimeStyles.AssumeUniversal);
        if (notOnOrAfter <= now - Skew) return null;
        var audiences = conditions.SelectNodes("saml:AudienceRestriction/saml:Audience", ns)!.Cast<XmlNode>().Select(a => a.InnerText.Trim()).ToList();
        if (!audiences.Contains(p.SpEntityId ?? string.Empty)) return null;

        var confirmation = assertion.SelectSingleNode("saml:Subject/saml:SubjectConfirmation[@Method='urn:oasis:names:tc:SAML:2.0:cm:bearer']/saml:SubjectConfirmationData", ns) as XmlElement;
        if (confirmation is null || confirmation.GetAttribute("Recipient") != p.AcsUrl || confirmation.GetAttribute("InResponseTo") != requestId) return null;
        if (!confirmation.HasAttribute("NotOnOrAfter") || DateTimeOffset.Parse(confirmation.GetAttribute("NotOnOrAfter"), null, System.Globalization.DateTimeStyles.AssumeUniversal) <= now - Skew) return null;

        // One use only.
        foreach (var old in SeenAssertions.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList()) SeenAssertions.TryRemove(old, out _);
        var assertionId = assertion.GetAttribute("ID");
        if (string.IsNullOrEmpty(assertionId) || !SeenAssertions.TryAdd(p.Key + "|" + assertionId, notOnOrAfter.AddMinutes(5))) return null;

        if (string.IsNullOrWhiteSpace(p.UserClaim))
        {
            return assertion.SelectSingleNode("saml:Subject/saml:NameID", ns)?.InnerText.Trim();
        }

        return assertion.SelectSingleNode($"saml:AttributeStatement/saml:Attribute[@Name='{p.UserClaim.Replace("'", string.Empty)}']/saml:AttributeValue", ns)?.InnerText.Trim();
    }

    /// <summary>The signed element (the response or the assertion) once its signature checks out against the provider's certificate; otherwise null.</summary>
    private static XmlElement? VerifiedSignedElement(SsoProviderOptions p, XmlDocument document, XmlElement response, XmlElement assertion, XmlNamespaceManager ns)
    {
        X509Certificate2 certificate;
        try
        {
            var text = p.IdpCertificate!.Replace("-----BEGIN CERTIFICATE-----", string.Empty).Replace("-----END CERTIFICATE-----", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
            certificate = new X509Certificate2(Convert.FromBase64String(text));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }

        foreach (var holder in new[] { assertion, response })
        {
            var signatures = holder.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Where(e => e.LocalName == "Signature" && e.NamespaceURI == SignedXml.XmlDsigNamespaceUrl).ToList();
            if (signatures.Count != 1) continue;

            var signed = new SignedXml(document);
            signed.LoadXml(signatures[0]);
            if (signed.SignedInfo?.References.Count != 1) continue;
            var reference = (Reference)signed.SignedInfo.References[0]!;
            var id = holder.GetAttribute("ID");
            if (string.IsNullOrEmpty(id) || reference.Uri != "#" + id) continue;
            // The id must name exactly one element, or the reference could point somewhere else.
            if (document.SelectNodes($"//*[@ID='{id.Replace("'", string.Empty)}']")!.Count != 1) continue;
            if (!signed.CheckSignature(certificate, true)) continue;
            return holder;
        }

        return null;
    }

    // --- the person ---

    private async Task<Result<SsoCompleted>> SignInAsync(SsoProviderOptions p, string? value, string returnUrl, CancellationToken ct)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return Result.Failure<SsoCompleted>(Error.Unauthorized(Failed));

        User? user;
        if (p.MatchBy == SsoMatchBy.Email)
        {
            var tenant = string.IsNullOrWhiteSpace(p.TenantSlug) ? null : await repository.GetTenantBySlugAsync(p.TenantSlug, ct);
            user = tenant is null && !string.IsNullOrWhiteSpace(p.TenantSlug) ? null : await repository.FindUserByEmailAsync(tenant?.Id, value, ct);
        }
        else
        {
            user = await repository.FindUserByLoginAsync(value, p.TenantSlug, ct);
        }

        if (user is null || !user.IsActive || user.IsAccessExpired(DateTimeOffset.UtcNow) || user.Kind == UserKind.ServiceAccount)
        {
            await platformService.AuditForAsync(user?.TenantId, user?.Id, "identity.sso_failed", nameof(User), user?.Id.ToString(), $"{p.Key}: {(user is null ? "no such user" : "not allowed")}", ct);
            return Result.Failure<SsoCompleted>(Error.Unauthorized("حساب شما در سامانه تعریف نشده یا غیرفعال است؛ با مدیر سامانه تماس بگیرید."));
        }

        var session = await sessionIssuer.IssueAsync(user, ct);
        if (session.SessionLimit is not null)
        {
            return Result.Failure<SsoCompleted>(Error.Unauthorized("سقف نشست هم‌زمان پر است؛ با نام کاربری و رمز وارد شوید تا نشست‌های قبلی را ببندید."));
        }

        await platformService.AuditForAsync(user.TenantId, user.Id, "identity.sso_login", nameof(User), user.Id.ToString(), p.Key, ct);
        return Result.Success(new SsoCompleted(session, returnUrl));
    }

    public Result<string> SamlMetadata(string key)
    {
        var p = Find(key, SsoProviderType.Saml);
        if (p is null || string.IsNullOrWhiteSpace(p.SpEntityId) || string.IsNullOrWhiteSpace(p.AcsUrl)) return Result.Failure<string>(Error.NotFound(Unavailable));
        return Result.Success($"""<?xml version="1.0" encoding="UTF-8"?><md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{Xml(p.SpEntityId)}"><md:SPSSODescriptor AuthnRequestsSigned="false" WantAssertionsSigned="true" protocolSupportEnumeration="{SamlProtocolNs}"><md:NameIDFormat>urn:oasis:names:tc:SAML:1.1:nameid-format:unspecified</md:NameIDFormat><md:AssertionConsumerService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST" Location="{Xml(p.AcsUrl)}" index="0" isDefault="true"/></md:SPSSODescriptor></md:EntityDescriptor>""");
    }
}
