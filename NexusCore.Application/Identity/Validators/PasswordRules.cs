using FluentValidation;

namespace NexusCore.Application.Identity.Validators;

/// <summary>
/// The password policy for passwords that are being set (never for signing in, so existing
/// accounts keep working until their owner changes the password).
/// </summary>
public static class PasswordRules
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    public const string Message =
        "رمز عبور باید حداقل ۱۰ کاراکتر باشد و دست‌کم از سه نوع کاراکتر (حرف کوچک، حرف بزرگ، رقم، نماد) تشکیل شود و رمزهای رایج مجاز نیست.";

    // The passwords that show up first in every leaked list, plus the keyboard and date patterns
    // people type when told to make one longer. Compared without case and without symbols.
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "password", "password1", "password12", "password123", "password1234", "passw0rd", "p@ssw0rd", "passw0rd1",
        "qwerty", "qwerty123", "qwertyuiop", "qwerty12345", "1qaz2wsx", "1q2w3e4r", "1q2w3e4r5t", "zaq12wsx",
        "admin", "admin123", "admin1234", "admin12345", "administrator", "admin@12345", "welcome", "welcome1", "welcome123",
        "letmein", "letmein123", "iloveyou", "monkey", "dragon", "football", "baseball", "abc123", "abcd1234", "abc12345",
        "123456", "1234567", "12345678", "123456789", "1234567890", "12345678910", "0123456789", "111111", "11111111",
        "tejaratbank", "tejarat", "tejarat123", "tejarat1234", "bank1234", "nexuscore", "nexus123", "changeme", "change-me",
        "test1234", "test12345", "default", "secret", "secret123", "trustno1", "freedom", "master", "sunshine",
    };

    public static bool IsStrong(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength || password.Length > MaxLength)
        {
            return false;
        }

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(ch => !char.IsLetterOrDigit(ch))) classes++;
        if (classes < 3)
        {
            return false;
        }

        // The same character over and over ("Aa1!Aa1!Aa1!" is still a repeat) is not strength.
        if (password.Distinct().Count() < 5)
        {
            return false;
        }

        var plain = new string(password.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return !Common.Contains(plain) && !Common.Contains(password.ToLowerInvariant());
    }

    public static IRuleBuilderOptions<T, string?> StrongPassword<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(IsStrong).WithMessage(Message);
}
