namespace Nexus.Reporting.Export.Application;

/// <summary>The words an exported report uses for the values the analytics hand over as English names. An unknown value is written as it came.</summary>
public static class PersianLabels
{
    public static string Health(string value) => value switch
    {
        "Green" => "سبز",
        "Amber" => "زرد",
        "Red" => "قرمز",
        "Unknown" => "نامشخص",
        _ => value
    };

    public static string ProjectStatus(string value) => value switch
    {
        "Draft" => "پیش‌نویس",
        "Active" => "فعال",
        "OnHold" => "متوقف",
        "Completed" => "تکمیل‌شده",
        "Archived" => "بایگانی‌شده",
        _ => value
    };

    public static string Severity(string value) => value switch
    {
        "High" => "بالا",
        "Medium" => "متوسط",
        _ => value
    };

    public static string AttentionKind(string value) => value switch
    {
        "ProjectOverdue" => "پروژه عقب‌افتاده از موعد پایان",
        "ProjectRed" => "پروژه با سلامت قرمز",
        "ProjectAtRisk" => "پروژه با سلامت زرد",
        "RiskWithoutResponsePlan" => "ریسک بالا بدون برنامه پاسخ",
        "ActionOverdue" => "اقدام معوق",
        _ => value
    };

    public static string SubjectType(string value) => value switch
    {
        "Project" => "پروژه",
        "Risk" => "ریسک",
        "Action" => "اقدام",
        _ => value
    };

    public static string Rating(string? value) => value switch
    {
        "Excellent" => "عالی",
        "Good" => "خوب",
        "NeedsImprovement" => "نیازمند بهبود",
        null => string.Empty,
        _ => value
    };

    public static string RiskLevel(string value) => value switch
    {
        "High" => "زیاد",
        "Medium" => "متوسط",
        "Low" => "کم",
        _ => value
    };

    public static string RiskResponse(string value) => value switch
    {
        "Planning" => "در حال برنامه‌ریزی",
        "InProgress" => "در حال اجرا",
        "OnHold" => "متوقف",
        "Completed" => "تکمیل‌شده",
        _ => value
    };

    public static string KpiType(string value) => value switch
    {
        "Lag" => "تأخیری",
        "Lead" => "پیشرو",
        _ => value
    };

    public static string AlignmentLevel(string value) => value switch
    {
        "None" => "—",
        "Low" => "کم",
        "Medium" => "متوسط",
        "High" => "زیاد",
        _ => value
    };

    public const string Unassigned = "تخصیص‌نیافته";
    public const string Total = "جمع";
}
