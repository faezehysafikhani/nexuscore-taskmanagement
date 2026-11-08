namespace Nexus.ProjectManagement.RiskManagement.Domain;

/// <summary>
/// The three bands (low / medium / high) a risk's probability and impact are talked about in, and how they sit on the 1-5 scores:
/// 1-2 low, 3 medium, 4-5 high; a band stored from a score reads back as 1, 3 or 5.
/// </summary>
public static class RiskLevels
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static IReadOnlyList<string> All { get; } = [Low, Medium, High];

    public static string FromScore(int score) => score <= 2 ? Low : score == 3 ? Medium : High;

    /// <summary>The score a band stands for, or null for a word that is not a band.</summary>
    public static int? ToScore(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        Low => 1,
        Medium => 3,
        High => 5,
        _ => null
    };

    public static bool IsLevel(string? level) => ToScore(level) is not null;
}
