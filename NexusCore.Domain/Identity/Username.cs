using System.Text.RegularExpressions;

namespace NexusCore.Domain.Identity;

/// <summary>
/// What a username may look like. It starts with a letter, so it can never be mistaken for a
/// mobile number (or an email address) when someone signs in.
/// </summary>
public static partial class Username
{
    public const string Pattern = @"^[A-Za-z][A-Za-z0-9_.-]{2,63}$";

    public static bool IsValid(string? value) => value is not null && PatternRegex().IsMatch(value.Trim());

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex PatternRegex();
}
