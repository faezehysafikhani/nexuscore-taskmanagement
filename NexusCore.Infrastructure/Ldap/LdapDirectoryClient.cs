using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Ldap;

namespace NexusCore.Infrastructure.Ldap;

/// <summary>
/// LDAP over System.DirectoryServices.Protocols (works against Active Directory and standard
/// LDAP servers, on Windows and Linux). The connection test binds with the configured account
/// and runs a small search of the user base, so it proves the server, the credentials, the base
/// DN and the filter in one go. Nothing it logs or returns contains the bind password.
/// </summary>
public sealed class LdapDirectoryClient(ILogger<LdapDirectoryClient> logger) : ILdapDirectoryClient
{
    private const int SampleSize = 5;

    public Task<LdapTestResultDto> TestConnectionAsync(LdapConnectionSettings settings, CancellationToken cancellationToken) =>
        // The protocol API is synchronous; keep it off the request thread.
        Task.Run(() => Test(settings), cancellationToken);

    private LdapTestResultDto Test(LdapConnectionSettings settings)
    {
        var watch = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(settings.ConnectionTimeoutSeconds);
        try
        {
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(settings.Host, settings.Port, false, false))
            {
                Timeout = timeout,
            };
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            if (settings.UseSsl)
            {
                connection.SessionOptions.SecureSocketLayer = true;
            }

            if (settings.TrustServerCertificate)
            {
                // Only when the administrator asked for it (an internal CA the server does not trust).
                connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }

            if (settings.UseStartTls)
            {
                connection.SessionOptions.StartTransportLayerSecurity(null);
            }

            if (string.IsNullOrWhiteSpace(settings.BindUsername))
            {
                connection.AuthType = AuthType.Anonymous;
            }
            else
            {
                connection.AuthType = AuthType.Basic;
                connection.Credential = new NetworkCredential(BindName(settings), settings.BindPassword ?? string.Empty);
            }

            connection.Bind();

            var searchBase = settings.UserSearchBase ?? settings.BaseDn;
            if (searchBase is null)
            {
                return new LdapTestResultDto(true, "اتصال و احراز هویت با سرور LDAP موفق بود. (Base DN تعیین نشده است؛ جستجوی کاربران انجام نشد.)", null, watch.ElapsedMilliseconds);
            }

            var request = new SearchRequest(searchBase, settings.UserFilter, SearchScope.Subtree, "distinguishedName")
            {
                SizeLimit = SampleSize,
                TimeLimit = timeout,
            };

            int found;
            try
            {
                found = ((SearchResponse)connection.SendRequest(request, timeout)).Entries.Count;
            }
            catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.SizeLimitExceeded)
            {
                found = ex.Response is SearchResponse partial ? partial.Entries.Count : SampleSize;
            }

            var sample = found >= SampleSize ? $"حداقل {SampleSize}" : found.ToString();
            return new LdapTestResultDto(true, $"اتصال موفق بود و {sample} کاربر با فیلتر تعیین‌شده در «{searchBase}» پیدا شد.", found, watch.ElapsedMilliseconds);
        }
        catch (LdapException ex)
        {
            logger.LogWarning("LDAP test against {Host}:{Port} failed with LDAP error {Code}.", settings.Host, settings.Port, ex.ErrorCode);
            return new LdapTestResultDto(false, ex.ErrorCode switch
            {
                49 => "نام کاربری یا رمز عبور اتصال (Bind) نادرست است.",
                81 => "سرور LDAP در دسترس نیست. نشانی، پورت و تنظیمات SSL را بررسی کنید.",
                85 => "زمان اتصال به سرور LDAP به پایان رسید.",
                91 => "اتصال به سرور LDAP برقرار نشد.",
                52 => "سرور LDAP در حال حاضر پاسخگو نیست.",
                _ => $"خطای LDAP ({ex.ErrorCode}): {ex.Message}",
            }, null, watch.ElapsedMilliseconds);
        }
        catch (DirectoryOperationException ex)
        {
            var code = ex.Response?.ResultCode;
            logger.LogWarning("LDAP test against {Host}:{Port} failed: {ResultCode}.", settings.Host, settings.Port, code);
            return new LdapTestResultDto(false, code switch
            {
                ResultCode.NoSuchObject => "مسیر جستجو (Base DN / User Search Base) در سرور وجود ندارد.",
                ResultCode.InsufficientAccessRights => "حساب اتصال اجازه جستجو در این مسیر را ندارد.",
                ResultCode.ProtocolError => "فیلتر کاربران معتبر نیست.",
                _ => $"خطای LDAP: {code}",
            }, null, watch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is DirectoryException or ArgumentException or PlatformNotSupportedException or DllNotFoundException)
        {
            logger.LogWarning("LDAP test against {Host}:{Port} failed: {ErrorType}.", settings.Host, settings.Port, ex.GetType().Name);
            return new LdapTestResultDto(false, ex is DllNotFoundException or PlatformNotSupportedException
                ? "کتابخانه LDAP روی سرور نصب نیست (در لینوکس بسته libldap لازم است)."
                : "تنظیمات اتصال LDAP نامعتبر است.", null, watch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// A bind name as the administrator typed it, completed for Active Directory: a distinguished
    /// name or DOMAIN\user is used as is; a bare account name gets the domain as a UPN suffix.
    /// </summary>
    private static string BindName(LdapConnectionSettings settings)
    {
        var name = settings.BindUsername!.Trim();
        if (name.Contains('=') || name.Contains('@') || name.Contains('\\') || string.IsNullOrWhiteSpace(settings.Domain))
        {
            return name;
        }

        return $"{name}@{settings.Domain.Trim()}";
    }
}
