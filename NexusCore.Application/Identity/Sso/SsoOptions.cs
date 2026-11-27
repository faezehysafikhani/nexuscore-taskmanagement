namespace NexusCore.Application.Identity.Sso;

public enum SsoProviderType
{
    Oidc = 0,
    Saml = 1,
}

/// <summary>Which field of the user the identity provider's answer is matched to.</summary>
public enum SsoMatchBy
{
    Username = 0,
    Email = 1,
    Phone = 2,
}

/// <summary>
/// Single sign-on (CAT-226): <c>Identity:Sso</c> in appsettings. Providers sign people in that already exist in the
/// system (no account is created from an identity provider's answer); the provider only proves who they are.
/// </summary>
public sealed class SsoOptions
{
    public const string SectionName = "Identity:Sso";

    public List<SsoProviderOptions> Providers { get; set; } = [];

    /// <summary>Where the browser may be sent after sign-in (the UI's addresses, e.g. https://tm.example.ir). A requested return address must start with one of these.</summary>
    public List<string> AllowedReturnUrls { get; set; } = [];

    /// <summary>Used when no (allowed) return address was asked for.</summary>
    public string DefaultReturnUrl { get; set; } = "/";

    /// <summary>Minutes a started sign-in may take (the time on the identity provider's page).</summary>
    public int StateMinutes { get; set; } = 10;
}

public sealed class SsoProviderOptions
{
    /// <summary>The short name in the address: /api/identity/auth/sso/{Key}/start.</summary>
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SsoProviderType Type { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>The organization the signed-in people belong to (its slug); null when the system has one organization.</summary>
    public string? TenantSlug { get; set; }

    public SsoMatchBy MatchBy { get; set; } = SsoMatchBy.Username;

    /// <summary>The claim (OIDC) or attribute (SAML) that carries the matched value. OIDC defaults: preferred_username / email / phone_number. SAML default: the NameID.</summary>
    public string? UserClaim { get; set; }

    // --- OpenID Connect (authorization code flow with PKCE) ---
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string Scopes { get; set; } = "openid profile email";

    /// <summary>The address registered at the provider; ends with /api/identity/auth/sso/{Key}/callback.</summary>
    public string? RedirectUri { get; set; }
    public bool RequireHttpsMetadata { get; set; } = true;

    // --- SAML 2.0 (web browser SSO: redirect out, POST back) ---
    public string? IdpSsoUrl { get; set; }
    public string? IdpEntityId { get; set; }

    /// <summary>The provider's signing certificate (base64 of the .cer file, or PEM). Assertions not signed with it are refused.</summary>
    public string? IdpCertificate { get; set; }
    public string? SpEntityId { get; set; }

    /// <summary>The address the provider posts the answer to; ends with /api/identity/auth/sso/{Key}/acs.</summary>
    public string? AcsUrl { get; set; }
}
