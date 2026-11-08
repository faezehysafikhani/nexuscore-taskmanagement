namespace Nexus.ProjectManagement.RiskManagement.Domain;

/// <summary>The wording and colours a project gets until it sets its own.</summary>
public static class RiskDefaults
{
    public static IReadOnlyList<RiskLevelDefinition> Definitions { get; } =
    [
        new("probability", "high", "بالای ۶۰٪", "احتمال وقوع بسیار بالا در طول اجرای پروژه (احتمال رخداد بیش از ۶۰٪)"),
        new("probability", "medium", "بین ۳۰٪ تا ۶۰٪", "احتمال وقوع متوسط و محتمل در طول اجرای پروژه (۳۰٪ الی ۶۰٪)"),
        new("probability", "low", "کمتر از ۳۰٪", "احتمال وقوع کم و نامحتمل (کمتر از ۳۰٪)"),
        new("impact", "high", "بالای ۶۰٪", "اثر شدید و بحرانی بر زمان، هزینه، کیفیت یا اهداف اصلی پروژه"),
        new("impact", "medium", "بین ۳۰٪ تا ۶۰٪", "اثر متوسط و قابل مدیریت بر برنامه زمان‌بندی یا بودجه پروژه"),
        new("impact", "low", "کمتر از ۳۰٪", "اثر جزئی، ناچیز و قابل اغماض بر فعالیت‌ها یا هزینه‌ها")
    ];

    public static IReadOnlyList<RiskMatrixColor> MatrixColors { get; } =
    [
        new("low", "low", "green"), new("low", "medium", "green"), new("medium", "low", "green"),
        new("low", "high", "yellow"), new("medium", "medium", "yellow"), new("high", "low", "yellow"),
        new("medium", "high", "red"), new("high", "medium", "red"), new("high", "high", "red")
    ];
}

/// <summary>One of the six bands (axis "probability" or "impact", level "low" / "medium" / "high"): how it is worded.</summary>
public sealed record RiskLevelDefinition(string Axis, string Level, string Range, string Description);

/// <summary>The colour ("green", "yellow" or "red") of one probability x impact cell.</summary>
public sealed record RiskMatrixColor(string Probability, string Impact, string Color);
