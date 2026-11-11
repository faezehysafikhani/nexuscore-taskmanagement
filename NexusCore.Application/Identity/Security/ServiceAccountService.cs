using System.Security.Cryptography;
using System.Text;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Services;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Security;

public interface IServiceAccountService
{
    Task<Result<IReadOnlyList<ServiceAccountDto>>> ListAsync(CancellationToken cancellationToken);
    Task<Result<ServiceAccountCreatedDto>> CreateAsync(CreateServiceAccountRequest request, CancellationToken cancellationToken);
    Task<Result<ApiKeyWithSecretDto>> AddKeyAsync(Guid accountId, CreateApiKeyRequest request, CancellationToken cancellationToken);
    Task<Result> RevokeKeyAsync(Guid accountId, Guid keyId, CancellationToken cancellationToken);

    /// <summary>Exchanges an API key for an ordinary access token (and refresh token) of its service account.</summary>
    Task<Result<AuthResponse>> ExchangeAsync(ApiKeyTokenRequest request, CancellationToken cancellationToken);

    Task<Result<UserDto>> SetKindAsync(Guid userId, SetUserKindRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Service accounts and their API keys (phase 9b). A service account is a user of kind ServiceAccount with no password, no
/// username and no mobile number, so it cannot sign in by any other way; its permissions are given like anyone's. A key
/// looks like <c>tmk_&lt;id&gt;.&lt;secret&gt;</c>, is shown once, and only the hash of its secret is kept.
/// </summary>
public sealed class ServiceAccountService(
    ISecurityRepository security,
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IPlatformService platformService,
    AuthSessionIssuer sessionIssuer) : IServiceAccountService
{
    public const string KeyPrefix = "tmk_";
    public static readonly Error InvalidKey = Error.Unauthorized("کلید API معتبر نیست.");

    private static ApiKeyDto ToDto(ApiKey k, DateTimeOffset now) => new(k.Id, k.Name, k.CreatedAtUtc, k.ExpiresAtUtc, k.LastUsedAtUtc, !k.IsUsableAt(now));

    private static string Compose(Guid keyId, string secret) => $"{KeyPrefix}{keyId:N}.{secret}";

    public async Task<Result<IReadOnlyList<ServiceAccountDto>>> ListAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<ServiceAccountDto>>(Error.Unauthorized());
        }

        var accounts = await security.ListServiceAccountsAsync(tenantId, cancellationToken);
        var keys = (await security.ListApiKeysAsync(accounts.Select(a => a.Id).ToList(), cancellationToken)).ToLookup(k => k.UserId);
        var now = DateTimeOffset.UtcNow;
        return Result.Success<IReadOnlyList<ServiceAccountDto>>(accounts
            .Select(a => new ServiceAccountDto(a.Id, a.DisplayName, a.IsActive, keys[a.Id].OrderByDescending(k => k.CreatedAtUtc).Select(k => ToDto(k, now)).ToList())).ToList());
    }

    public async Task<Result<ServiceAccountCreatedDto>> CreateAsync(CreateServiceAccountRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<ServiceAccountCreatedDto>(Error.Unauthorized());
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
        {
            return Result.Failure<ServiceAccountCreatedDto>(Error.Validation("نام حساب سرویس را (حداکثر ۱۰۰ کاراکتر) وارد کنید."));
        }

        // No usable password: a random one nobody knows. With no username or mobile number there is nothing to sign in with anyway.
        var user = new User(Guid.NewGuid(), tenantId, null, request.Name.Trim(), passwordHasher.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))), true);
        user.SetKind(UserKind.ServiceAccount);
        user.UpdateContactDetails(null, null, notifySms: false);
        await repository.AddUserAsync(user, cancellationToken);
        var key = await NewKeyAsync(user, "default", request.KeyExpiresInDays, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditForAsync(tenantId, currentUser.UserId, "identity.service_account_created", nameof(User), user.Id.ToString(), user.DisplayName, cancellationToken);
        return Result.Success(new ServiceAccountCreatedDto(new ServiceAccountDto(user.Id, user.DisplayName, true, [key.Key]), key.Secret));
    }

    private async Task<ApiKeyWithSecretDto> NewKeyAsync(User user, string name, int? expiresInDays, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var key = new ApiKey(id, user.Id, user.TenantId, name, passwordHasher.HashToken(secret), now, expiresInDays is > 0 ? now.AddDays(expiresInDays.Value) : null, currentUser.UserId);
        await security.AddApiKeyAsync(key, cancellationToken);
        return new ApiKeyWithSecretDto(ToDto(key, now), Compose(id, secret));
    }

    private async Task<User?> AccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(accountId, cancellationToken);
        return user is not null && user.Kind == UserKind.ServiceAccount && currentUser.TenantId == user.TenantId ? user : null;
    }

    public async Task<Result<ApiKeyWithSecretDto>> AddKeyAsync(Guid accountId, CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var user = await AccountAsync(accountId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<ApiKeyWithSecretDto>(Error.NotFound("حساب سرویس یافت نشد."));
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100 || request.ExpiresInDays is < 1 or > 3650)
        {
            return Result.Failure<ApiKeyWithSecretDto>(Error.Validation("نام کلید و مدت اعتبار (۱ تا ۳۶۵۰ روز، یا خالی) را درست وارد کنید."));
        }

        var key = await NewKeyAsync(user, request.Name, request.ExpiresInDays, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditForAsync(user.TenantId, currentUser.UserId, "identity.api_key_created", nameof(User), user.Id.ToString(), request.Name, cancellationToken);
        return Result.Success(key);
    }

    public async Task<Result> RevokeKeyAsync(Guid accountId, Guid keyId, CancellationToken cancellationToken)
    {
        var user = await AccountAsync(accountId, cancellationToken);
        var key = user is null ? null : await security.GetApiKeyAsync(keyId, cancellationToken);
        if (user is null || key is null || key.UserId != user.Id)
        {
            return Result.Failure(Error.NotFound("کلید یافت نشد."));
        }

        key.Revoke(DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditForAsync(user.TenantId, currentUser.UserId, "identity.api_key_revoked", nameof(User), user.Id.ToString(), key.Name, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<AuthResponse>> ExchangeAsync(ApiKeyTokenRequest request, CancellationToken cancellationToken)
    {
        var raw = request.ApiKey?.Trim() ?? string.Empty;
        var dot = raw.IndexOf('.');
        if (!raw.StartsWith(KeyPrefix, StringComparison.Ordinal) || dot < 0 || !Guid.TryParseExact(raw[KeyPrefix.Length..dot], "N", out var keyId))
        {
            return Result.Failure<AuthResponse>(InvalidKey);
        }

        var secret = raw[(dot + 1)..];
        var key = await security.GetApiKeyAsync(keyId, cancellationToken);
        var user = key is null ? null : await repository.GetUserByIdAsync(key.UserId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        // The same answer for every way it can fail, so a key's state is not revealed.
        if (key is null || user is null || !user.IsActive || user.Kind != UserKind.ServiceAccount || !key.IsUsableAt(now)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(passwordHasher.HashToken(secret)), Encoding.ASCII.GetBytes(key.SecretHash)))
        {
            return Result.Failure<AuthResponse>(InvalidKey);
        }

        key.MarkUsed(now);
        return Result.Success(await sessionIssuer.IssueAsync(user, cancellationToken, skipLimit: true));
    }

    public async Task<Result<UserDto>> SetKindAsync(Guid userId, SetUserKindRequest request, CancellationToken cancellationToken)
    {
        var user = await repository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null || currentUser.TenantId != user.TenantId)
        {
            return Result.Failure<UserDto>(Error.NotFound("کاربر یافت نشد."));
        }

        if (user.IsSystem || user.Kind == UserKind.ServiceAccount || request.Kind == UserKind.ServiceAccount)
        {
            return Result.Failure<UserDto>(Error.Validation("نوع حساب سرویس یا مدیر سامانه را نمی‌توان تغییر داد."));
        }

        user.SetKind(request.Kind);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditForAsync(user.TenantId, currentUser.UserId, "users.kind_changed", nameof(User), userId.ToString(), request.Kind.ToString(), cancellationToken);
        return Result.Success(IdentityMappings.ToUserDto(user));
    }
}
