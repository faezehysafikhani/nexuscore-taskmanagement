using Microsoft.Extensions.DependencyInjection;
using NexusCore.Application.Identity;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Ldap;

public sealed record LdapSyncRequest(
    LdapUserAttributes? Attributes = null,
    /// <summary>Switch off the accounts that came from the directory and are no longer in it (or are disabled there).</summary>
    bool DeactivateMissing = true,
    int MaxUsers = 5000);

public enum LdapSyncAction { Create, Update, Deactivate, Reactivate, Unchanged, Skipped }

public sealed record LdapSyncRow(string? Username, string? DisplayName, LdapSyncAction Action, string? Reason);

public sealed record LdapSyncReport(bool Applied, int Created, int Updated, int Deactivated, int Reactivated, int Unchanged, int Skipped, IReadOnlyList<LdapSyncRow> Rows);

public interface ILdapSyncService
{
    /// <summary>What a sync would do, changing nothing.</summary>
    Task<Result<LdapSyncReport>> PreviewAsync(LdapSyncRequest request, CancellationToken cancellationToken);

    /// <summary>Does it. Users that come from the directory sign in with their directory password.</summary>
    Task<Result<LdapSyncReport>> ApplyAsync(LdapSyncRequest request, CancellationToken cancellationToken);
}

/// <summary>Signs in with the directory password for a user that came from the directory.</summary>
public interface ILdapAuthenticator
{
    Task<bool> VerifyAsync(User user, string password, CancellationToken cancellationToken);
}

public sealed class LdapAuthenticator(IServiceProvider services) : ILdapAuthenticator
{
    public async Task<bool> VerifyAsync(User user, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(user.LdapDn) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        // Looked up now, not at construction: a host with no directory settings can still compose the sign-in.
        var connection = await services.GetRequiredService<ILdapSettingsService>().GetConnectionAsync(user.TenantId, cancellationToken);
        return connection is not null && await services.GetRequiredService<ILdapDirectoryClient>().AuthenticateAsync(connection, user.LdapDn, password, cancellationToken);
    }
}

/// <summary>
/// HR sync (CAT-9): the people of the directory become users - created, updated, switched off when they leave - without typing them
/// in. A person needs a username that is a 10-digit national code (the product's sign-in rule; choose the attribute that holds it)
/// and a mobile number is used when it is valid. The organization's user limit is respected. Nobody is ever deleted, and the
/// built-in administrator, guests and service accounts are never touched.
/// </summary>
public sealed class LdapSyncService(
    ILdapSettingsService settings,
    ILdapDirectoryClient directory,
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IPlatformService platformService,
    IUserQuota? userQuota = null) : ILdapSyncService
{
    public Task<Result<LdapSyncReport>> PreviewAsync(LdapSyncRequest request, CancellationToken cancellationToken) => RunAsync(request, apply: false, cancellationToken);

    public Task<Result<LdapSyncReport>> ApplyAsync(LdapSyncRequest request, CancellationToken cancellationToken) => RunAsync(request, apply: true, cancellationToken);

    private async Task<Result<LdapSyncReport>> RunAsync(LdapSyncRequest request, bool apply, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<LdapSyncReport>(Error.Unauthorized());
        }

        var connection = await settings.GetConnectionAsync(tenantId, cancellationToken);
        if (connection is null)
        {
            return Result.Failure<LdapSyncReport>(Error.Validation("LDAP در تنظیمات فعال نیست."));
        }

        var found = await directory.SearchUsersAsync(connection, request.Attributes ?? new LdapUserAttributes(), Math.Clamp(request.MaxUsers, 1, 20000), cancellationToken);
        if (!found.Success)
        {
            return Result.Failure<LdapSyncReport>(Error.Validation(found.Message));
        }

        var rows = new List<LdapSyncRow>();
        var seenDns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var added = 0;
        foreach (var entry in found.Entries)
        {
            seenDns.Add(entry.Dn);
            var row = await HandleAsync(tenantId, entry, apply, added, cancellationToken);
            if (row.Action == LdapSyncAction.Create)
            {
                added++;
                created++;
            }

            rows.Add(row);
        }

        // Accounts that came from the directory and are not in it any more.
        if (request.DeactivateMissing && found.Entries.Count > 0)
        {
            foreach (var gone in await repository.ListLdapUsersAsync(tenantId, cancellationToken))
            {
                if (gone.IsActive && !seenDns.Contains(gone.LdapDn!))
                {
                    if (apply)
                    {
                        gone.SetActive(false);
                    }

                    rows.Add(new LdapSyncRow(gone.Username, gone.DisplayName, LdapSyncAction.Deactivate, "در دایرکتوری نیست"));
                }
            }
        }

        if (apply)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await platformService.AuditAsync("users.ldap_sync", nameof(User), null, $"+{created}", cancellationToken);
        }

        int Count(LdapSyncAction a) => rows.Count(r => r.Action == a);
        return Result.Success(new LdapSyncReport(apply, Count(LdapSyncAction.Create), Count(LdapSyncAction.Update), Count(LdapSyncAction.Deactivate),
            Count(LdapSyncAction.Reactivate), Count(LdapSyncAction.Unchanged), Count(LdapSyncAction.Skipped), rows));
    }

    private async Task<LdapSyncRow> HandleAsync(Guid tenantId, LdapUserEntry entry, bool apply, int alreadyAdded, CancellationToken cancellationToken)
    {
        var username = entry.Username?.Trim();
        var display = entry.DisplayName ?? $"{entry.FirstName} {entry.LastName}".Trim();
        if (!Username.IsValid(username))
        {
            return new LdapSyncRow(username, display, LdapSyncAction.Skipped, "نام کاربری ده‌رقمی (کد ملی) نیست؛ ویژگی نام کاربری را در تنظیم همگام‌سازی انتخاب کنید");
        }

        var phone = PhoneNumber.Normalize(entry.Phone);
        var user = await repository.FindUserByLdapDnAsync(tenantId, entry.Dn, cancellationToken)
                   ?? await repository.FindUserByUsernameAsync(tenantId, username!, cancellationToken);

        if (user is null)
        {
            if (entry.IsDisabled)
            {
                return new LdapSyncRow(username, display, LdapSyncAction.Skipped, "در دایرکتوری غیرفعال است");
            }

            if (userQuota is not null && alreadyAdded >= 0 && await userQuota.CheckCanAddUserAsync(tenantId, cancellationToken) is { } refused)
            {
                return new LdapSyncRow(username, display, LdapSyncAction.Skipped, refused.Message);
            }

            if (phone is not null && await repository.FindPhoneNumberOwnerAsync(tenantId, phone, null, cancellationToken) is not null)
            {
                phone = null;
            }

            if (apply)
            {
                // No usable local password: signing in is checked against the directory.
                var fresh = new User(Guid.NewGuid(), tenantId, NullIfEmpty(entry.Email), string.IsNullOrWhiteSpace(display) ? username! : display,
                    passwordHasher.HashPassword(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))), true);
                fresh.SetName(entry.FirstName ?? display, entry.LastName ?? string.Empty);
                fresh.UpdateContactDetails(username, phone, notifySms: phone is not null);
                fresh.SetLdapDn(entry.Dn);
                var role = await repository.GetRoleByNameAsync(tenantId, DefaultUserRole.Name, cancellationToken);
                if (role is not null)
                {
                    fresh.AssignRole(role.Id);
                }

                await repository.AddUserAsync(fresh, cancellationToken);
            }

            return new LdapSyncRow(username, display, LdapSyncAction.Create, null);
        }

        if (user.IsSystem || user.Kind != UserKind.Normal)
        {
            return new LdapSyncRow(username, display, LdapSyncAction.Skipped, "حساب سیستمی، مهمان یا سرویس است");
        }

        if (user.LdapDn is null && user.Username != username)
        {
            return new LdapSyncRow(username, display, LdapSyncAction.Skipped, "کاربر محلی با این مشخصات هست");
        }

        if (entry.IsDisabled)
        {
            if (!user.IsActive)
            {
                return new LdapSyncRow(username, display, LdapSyncAction.Unchanged, null);
            }

            if (apply)
            {
                user.SetActive(false);
            }

            return new LdapSyncRow(username, display, LdapSyncAction.Deactivate, "در دایرکتوری غیرفعال است");
        }

        // The name the account would have after SetName, so a second run finds nothing to change.
        var first = entry.FirstName ?? display;
        var last = entry.LastName ?? string.Empty;
        var wantedName = $"{first} {last}".Trim();
        var changed = (!string.IsNullOrWhiteSpace(wantedName) && user.DisplayName != wantedName) || user.LdapDn != entry.Dn;
        if (apply)
        {
            if (user.LdapDn != entry.Dn)
            {
                user.SetLdapDn(entry.Dn);
            }

            if (!string.IsNullOrWhiteSpace(wantedName) && user.DisplayName != wantedName)
            {
                user.SetName(first, last);
            }
        }

        if (!user.IsActive)
        {
            if (apply)
            {
                user.SetActive(true);
            }

            return new LdapSyncRow(username, display, LdapSyncAction.Reactivate, null);
        }

        return new LdapSyncRow(username, display, changed ? LdapSyncAction.Update : LdapSyncAction.Unchanged, null);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
