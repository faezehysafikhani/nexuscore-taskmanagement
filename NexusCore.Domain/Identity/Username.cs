using System.Text.RegularExpressions;

namespace NexusCore.Domain.Identity;

/// <summary>
/// Usernames are national codes (کد ملی): exactly 10 digits. Every new or changed username must
/// follow this rule. Accounts from before the rule keep their existing username (letters first)
/// until it is changed, and can still sign in with it.
/// </summary>
public static partial class Username
{
    public const string Pattern = @"^[0-9]{10}$";

    /// <summary>The rule for new and changed usernames.</summary>
    public static bool IsValid(string? value) => value is not null && NationalCode().IsMatch(value);

    /// <summary>
    /// A real national code: 10 digits whose last is the check digit of the first nine (and not all
    /// the same digit). Only for new and changed usernames; signing in still accepts any 10 digits,
    /// so existing accounts keep working.
    /// </summary>
    public static bool IsValidNationalCode(string? value)
    {
        if (!IsValid(value) || value!.Distinct().Count() == 1)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (value[i] - '0') * (10 - i);
        }

        var remainder = sum % 11;
        var check = value[9] - '0';
        return remainder < 2 ? check == remainder : check == 11 - remainder;
    }

    /// <summary>A username created before the national-code rule: starts with a letter.</summary>
    public static bool IsLegacy(string? value) => value is not null && Legacy().IsMatch(value.Trim());

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex NationalCode();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Legacy();
}
