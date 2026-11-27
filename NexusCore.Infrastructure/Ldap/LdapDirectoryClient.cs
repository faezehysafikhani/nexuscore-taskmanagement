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

    public Task<LdapSearchResult> SearchUsersAsync(LdapConnectionSettings settings, LdapUserAttributes attributes, int maxEntries, CancellationToken cancellationToken) =>
        Task.Run(() => Search(settings, attributes, maxEntries), cancellationToken);

    public Task<bool> AuthenticateAsync(LdapConnectionSettings settings, string userDn, string password, CancellationToken cancellationToken) =>
        Task.Run(() => Authenticate(settings, userDn, password), cancellationToken);

    private LdapConnection Open(LdapConnectionSettings settings, string? bindName, string? bindPassword)
    {
        var timeout = TimeSpan.FromSeconds(settings.ConnectionTimeoutSeconds);
        var connection = new LdapConnection(new LdapDirectoryIdentifier(settings.Host, settings.Port, false, false)) { Timeout = timeout };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        if (settings.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }

        if (settings.TrustServerCertificate)
        {
            connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
        }

        if (settings.UseStartTls)
        {
            connection.SessionOptions.StartTransportLayerSecurity(null);
        }

        if (string.IsNullOrWhiteSpace(bindName))
        {
            connection.AuthType = AuthType.Anonymous;
        }
        else
        {
            connection.AuthType = AuthType.Basic;
            connection.Credential = new NetworkCredential(bindName, bindPassword ?? string.Empty);
        }

        connection.Bind();
        return connection;
    }

    private bool Authenticate(LdapConnectionSettings settings, string userDn, string password)
    {
        // An empty password would be an anonymous bind, which always "succeeds": never try one.
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(userDn))
        {
            return false;
        }

        try
        {
            using var connection = Open(settings, userDn, password);
            return true;
        }
        catch (LdapException ex)
        {
            if (ex.ErrorCode != 49)
            {
                logger.LogWarning("LDAP sign-in check against {Host}:{Port} failed with LDAP error {Code}.", settings.Host, settings.Port, ex.ErrorCode);
            }

            return false;
        }
        catch (Exception ex) when (ex is DirectoryException or ArgumentException or PlatformNotSupportedException or DllNotFoundException)
        {
            logger.LogWarning("LDAP sign-in check against {Host}:{Port} failed: {ErrorType}.", settings.Host, settings.Port, ex.GetType().Name);
            return false;
        }
    }

    private LdapSearchResult Search(LdapConnectionSettings settings, LdapUserAttributes attributes, int maxEntries)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(settings.ConnectionTimeoutSeconds, 30));
        try
        {
            using var connection = Open(settings, string.IsNullOrWhiteSpace(settings.BindUsername) ? null : BindName(settings), settings.BindPassword);
            var searchBase = settings.UserSearchBase ?? settings.BaseDn;
            if (searchBase is null)
            {
                return new LdapSearchResult(false, "Base DN یا مسیر جستجوی کاربران تعیین نشده است.", []);
            }

            string[] wanted = [attributes.Username, attributes.DisplayName, attributes.FirstName, attributes.LastName, attributes.Email, attributes.Phone, "userAccountControl", "distinguishedName"];
            var request = new SearchRequest(searchBase, settings.UserFilter, SearchScope.Subtree, wanted) { TimeLimit = timeout };
            // Paged: a directory refuses to return thousands of people in one answer.
            var pager = new PageResultRequestControl(500);
            request.Controls.Add(pager);

            var entries = new List<LdapUserEntry>();
            while (entries.Count < maxEntries)
            {
                var response = (SearchResponse)connection.SendRequest(request, timeout);
                foreach (SearchResultEntry entry in response.Entries)
                {
                    if (entries.Count >= maxEntries)
                    {
                        break;
                    }

                    var flags = int.TryParse(First(entry, "userAccountControl"), out var uac) ? uac : 0;
                    entries.Add(new LdapUserEntry(
                        entry.DistinguishedName, First(entry, attributes.Username), First(entry, attributes.DisplayName), First(entry, attributes.FirstName),
                        First(entry, attributes.LastName), First(entry, attributes.Email), First(entry, attributes.Phone), (flags & 0x2) != 0));
                }

                var control = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
                if (control is null || control.Cookie.Length == 0)
                {
                    break;
                }

                pager.Cookie = control.Cookie;
            }

            return new LdapSearchResult(true, $"{entries.Count} کاربر خوانده شد.", entries);
        }
        catch (LdapException ex)
        {
            logger.LogWarning("LDAP sync search against {Host}:{Port} failed with LDAP error {Code}.", settings.Host, settings.Port, ex.ErrorCode);
            return new LdapSearchResult(false, ex.ErrorCode == 49 ? "نام کاربری یا رمز عبور اتصال (Bind) نادرست است." : $"اتصال به سرور LDAP ناموفق بود (کد {ex.ErrorCode}).", []);
        }
        catch (Exception ex) when (ex is DirectoryException or ArgumentException or PlatformNotSupportedException or DllNotFoundException)
        {
            logger.LogWarning("LDAP sync search against {Host}:{Port} failed: {ErrorType}.", settings.Host, settings.Port, ex.GetType().Name);
            return new LdapSearchResult(false, "خواندن کاربران از LDAP ممکن نشد. تنظیمات و گزارش رویدادهای سرور را بررسی کنید.", []);
        }
    }

    private static string? First(SearchResultEntry entry, string attribute) =>
        entry.Attributes.Contains(attribute) && entry.Attributes[attribute].Count > 0 ? entry.Attributes[attribute][0]?.ToString() : null;

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
            // The server's own text stays in the log; the administrator gets a Persian message.
            logger.LogWarning("LDAP test against {Host}:{Port} failed with LDAP error {Code}: {Message}", settings.Host, settings.Port, ex.ErrorCode, ex.Message);
            return new LdapTestResultDto(false, ex.ErrorCode switch
            {
                49 => "نام کاربری یا رمز عبور اتصال (Bind) نادرست است.",
                81 => "سرور LDAP در دسترس نیست. نشانی، پورت و تنظیمات SSL را بررسی کنید.",
                85 => "زمان اتصال به سرور LDAP به پایان رسید.",
                91 => "اتصال به سرور LDAP برقرار نشد.",
                52 => "سرور LDAP در حال حاضر پاسخگو نیست.",
                _ => $"اتصال به سرور LDAP ناموفق بود (کد خطای {ex.ErrorCode}). جزئیات در گزارش رویدادهای سرور ثبت شد.",
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
                _ => "سرور LDAP درخواست را نپذیرفت. جزئیات در گزارش رویدادهای سرور ثبت شد.",
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
