using System.Globalization;

namespace Nexus.ProjectManagement.Contracts.Application.Persian;

/// <summary>Jalali (Persian) dates for display; the system stores plain Gregorian dates.</summary>
public static class PersianDates
{
    private static readonly PersianCalendar Calendar = new();

    /// <summary>"۱۴۰۵/۰۱/۱۲" for the given date.</summary>
    public static string Format(DateOnly date)
    {
        var text = $"{Calendar.GetYear(date.ToDateTime(TimeOnly.MinValue)):0000}/" +
                   $"{Calendar.GetMonth(date.ToDateTime(TimeOnly.MinValue)):00}/" +
                   $"{Calendar.GetDayOfMonth(date.ToDateTime(TimeOnly.MinValue)):00}";
        return PersianNumbers.ToPersianDigits(text);
    }

    public static string? Format(DateOnly? date) => date is { } value ? Format(value) : null;
}
