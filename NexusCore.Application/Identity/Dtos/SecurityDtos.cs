namespace NexusCore.Application.Identity.Dtos;

/// <summary>The sign-in waits for the code sent by SMS to MaskedPhone; send it with the ChallengeId to /auth/login/mfa.</summary>
public sealed record MfaChallengeDto(Guid ChallengeId, string MaskedPhone, int ExpiresInSeconds);

public sealed record CompleteMfaRequest(Guid ChallengeId, string Code);
public sealed record ConfirmMfaEnableRequest(Guid ChallengeId, string Code);
public sealed record DisableMfaRequest(string Password);

public sealed record CreateServiceAccountRequest(string Name, int? KeyExpiresInDays = null);
public sealed record CreateApiKeyRequest(string Name, int? ExpiresInDays = null);
public sealed record ApiKeyDto(Guid Id, string Name, DateTimeOffset CreatedAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? LastUsedAtUtc, bool Revoked);
public sealed record ApiKeyWithSecretDto(ApiKeyDto Key, string Secret);
public sealed record ServiceAccountDto(Guid Id, string Name, bool IsActive, IReadOnlyList<ApiKeyDto> Keys);
public sealed record ServiceAccountCreatedDto(ServiceAccountDto Account, string Secret);
public sealed record ApiKeyTokenRequest(string ApiKey);
public sealed record SetUserKindRequest(NexusCore.Domain.Identity.UserKind Kind);
