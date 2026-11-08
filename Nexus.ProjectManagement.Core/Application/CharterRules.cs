using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Application;

/// <summary>The checks <see cref="ProjectOptions.ValidateCharter"/> turns on. Each failure has its own code
/// (<c>project.charter.*</c>) so a client can show the message in the user's language.</summary>
public static class CharterRules
{
    private const int MinimumTextLength = 4;

    private static readonly HashSet<string> PlaceholderTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "test", "تست", "asdf", "qwer", "1234", "123456", "aaaa", "bbbb", "xxxx", "....", "---"
    };

    public static Error? Check(
        string name, Guid? managerUserId, Guid? organizationUnitId, DateOnly? startDate, DateOnly? endDate,
        string? goal, string? requirements, string? constraints, string? assumptions, string? description)
    {
        if (string.IsNullOrWhiteSpace(name)) return Missing("name", "The project name is required.");
        if (managerUserId is null) return Missing("manager", "The project manager is required.");
        if (organizationUnitId is null) return Missing("unit", "The owning unit is required.");
        if (startDate is null) return Missing("start_date", "The start date is required.");
        if (endDate is null) return Missing("end_date", "The end date is required.");
        if (string.IsNullOrWhiteSpace(goal)) return Missing("goal", "The project goal is required.");
        if (string.IsNullOrWhiteSpace(requirements)) return Missing("requirements", "The project requirements are required.");

        return Meaningless("goal", goal) ?? Meaningless("requirements", requirements)
            ?? Meaningless("constraints", constraints) ?? Meaningless("assumptions", assumptions) ?? Meaningless("description", description);
    }

    private static Error Missing(string field, string message) => new($"project.charter.{field}_required", message);

    private static Error? Meaningless(string field, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.Length < MinimumTextLength || PlaceholderTexts.Contains(trimmed)
            ? new Error($"project.charter.{field}_meaningless", $"The {field} text is not meaningful; describe it in real words.")
            : null;
    }
}
