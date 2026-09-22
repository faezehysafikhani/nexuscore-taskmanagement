using System.Text;

namespace NexusCore.Domain.Identity;

/// <summary>
/// The one canonical form of a mobile number, so the same number is stored, compared and
/// looked up identically however it was typed ("0912 123 4567", "+98 912-123-4567",
/// "00989121234567", "۰۹۱۲۱۲۳۴۵۶۷").
///
/// Iranian mobile numbers become 09xxxxxxxxx (the form the SMS gateway takes). Any other
/// number written with its country code (+ or 00) becomes +&lt;country code&gt;&lt;number&gt;.
/// Anything else - letters, landlines without a country code, wrong lengths - is not a valid
/// mobile number and normalizes to null.
/// </summary>
public static class PhoneNumber
{
    public const int MaxLength = 16; // "+" and up to 15 digits (E.164)

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new StringBuilder(value.Length);
        var international = false;
        foreach (var ch in value.Trim())
        {
            if (ch is >= '0' and <= '9')
            {
                digits.Append(ch);
            }
            else if (ch is >= '۰' and <= '۹')
            {
                digits.Append((char)('0' + (ch - '۰')));
            }
            else if (ch is >= '٠' and <= '٩')
            {
                digits.Append((char)('0' + (ch - '٠')));
            }
            else if (ch == '+' && digits.Length == 0 && !international)
            {
                international = true;
            }
            else if (ch is ' ' or '-' or '(' or ')' or '.' or '‌' or '‎' or '‏' or ' ')
            {
                // Separators people type or paste; they carry no meaning.
            }
            else
            {
                return null;
            }
        }

        var number = digits.ToString();
        if (!international && number.StartsWith("00", StringComparison.Ordinal))
        {
            international = true;
            number = number[2..];
        }

        if (international)
        {
            if (number.StartsWith("98", StringComparison.Ordinal))
            {
                // +98 9121234567, and the common "+98 09121234567".
                var national = number[2..];
                return IranianMobile(national.StartsWith('0') ? national : "0" + national);
            }

            return number.Length is >= 8 and <= 15 && number[0] != '0' ? "+" + number : null;
        }

        // Without a country code only Iranian mobile numbers are recognised:
        // 09121234567, 9121234567 and 989121234567.
        if (number.Length == 12 && number.StartsWith("989", StringComparison.Ordinal))
        {
            return IranianMobile("0" + number[2..]);
        }

        return IranianMobile(number.StartsWith('0') ? number : "0" + number);
    }

    public static bool IsValid(string? value) => Normalize(value) is not null;

    private static string? IranianMobile(string candidate) =>
        candidate.Length == 11 && candidate.StartsWith("09", StringComparison.Ordinal) ? candidate : null;
}
