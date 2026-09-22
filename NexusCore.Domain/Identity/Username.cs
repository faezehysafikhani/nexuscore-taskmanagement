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

    /// <summary>A username created before the national-code rule: starts with a letter.</summary>
    public static bool IsLegacy(string? value) => value is not null && Legacy().IsMatch(value.Trim());

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex NationalCode();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Legacy();
}
