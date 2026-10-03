using System.Globalization;
using System.Text;

namespace Nexus.ProjectManagement.Contracts.Application.Persian;

/// <summary>
/// Persian digits, amounts in words and money formatting - what a contract or an invoice needs to
/// be written the way Iranian paperwork is. Pure functions, no culture settings involved.
/// </summary>
public static class PersianNumbers
{
    private const string PersianDigits = "۰۱۲۳۴۵۶۷۸۹";
    private const string ArabicIndicDigits = "٠١٢٣٤٥٦٧٨٩";

    /// <summary>The largest amount that can be written in words (just under 10^15).</summary>
    public const long MaxValue = 999_999_999_999_999;

    private static readonly string[] Ones = ["صفر", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه"];
    private static readonly string[] Teens = ["ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده"];
    private static readonly string[] Tens = ["", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];
    private static readonly string[] Hundreds = ["", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد"];

    /// <summary>The scale words by thousands-group: units, thousand, million, billion, trillion.</summary>
    private static readonly string[] Scales = ["", "هزار", "میلیون", "میلیارد", "تریلیون"];

    /// <summary>Digits of any script (Latin, Persian, Arabic-Indic) written as plain 0-9, so that
    /// numbers typed in either alphabet compare equal and sort correctly.</summary>
    public static string ToLatinDigits(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var persian = PersianDigits.IndexOf(c);
            var arabic = ArabicIndicDigits.IndexOf(c);
            builder.Append(persian >= 0 ? (char)('0' + persian) : arabic >= 0 ? (char)('0' + arabic) : c);
        }

        return builder.ToString();
    }

    /// <summary>The digits written in Persian script: "1405/01" becomes "۱۴۰۵/۰۱".</summary>
    public static string ToPersianDigits(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in ToLatinDigits(text))
        {
            builder.Append(c is >= '0' and <= '9' ? PersianDigits[c - '0'] : c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// A whole number in words: 1250000 is "یک میلیون و دویست و پنجاه هزار". A scale of exactly one
    /// keeps its "یک" (the way cheques and contracts write it: "یک هزار", "یک میلیون"); a hundred is
    /// "صد". Negative numbers start with "منفی". Fractions are rounded away (amounts are in whole rials).
    /// </summary>
    public static string ToWords(decimal value)
    {
        var rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The number is too large to write in words.");
        }

        var number = (long)rounded;
        if (number == 0)
        {
            return Ones[0];
        }

        var parts = new List<string>();
        var remaining = Math.Abs(number);
        for (var scale = 0; remaining > 0; scale++, remaining /= 1000)
        {
            var group = (int)(remaining % 1000);
            if (group == 0)
            {
                continue;
            }

            var words = GroupWords(group);
            parts.Insert(0, scale == 0 ? words : $"{words} {Scales[scale]}");
        }

        var text = string.Join(" و ", parts);
        return number < 0 ? $"منفی {text}" : text;
    }

    /// <summary>An amount of rials in words: "یک میلیون و دویست و پنجاه هزار ریال".</summary>
    public static string RialsInWords(decimal rials) => $"{ToWords(rials)} ریال";

    /// <summary>
    /// The same amount in tomans (one toman is ten rials): a whole number of tomans reads
    /// "... تومان"; a remainder of rials follows ("... تومان و پنج ریال"), and an amount under one
    /// toman is just its rials.
    /// </summary>
    public static string TomansInWords(decimal rials)
    {
        var whole = (long)Math.Round(rials, 0, MidpointRounding.AwayFromZero);
        var magnitude = Math.Abs(whole);
        if (magnitude > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rials), "The number is too large to write in words.");
        }

        var tomans = magnitude / 10;
        var restRials = magnitude % 10;
        var sign = whole < 0 ? "منفی " : string.Empty;

        if (tomans == 0)
        {
            return restRials == 0 ? $"{Ones[0]} تومان" : $"{sign}{ToWords(restRials)} ریال";
        }

        return restRials == 0
            ? $"{sign}{ToWords(tomans)} تومان"
            : $"{sign}{ToWords(tomans)} تومان و {ToWords(restRials)} ریال";
    }

    /// <summary>Grouped digits in Persian script, e.g. 1250000 -> "۱,۲۵۰,۰۰۰" (a minus sign for negatives).</summary>
    public static string FormatMoney(decimal value)
    {
        var rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        return ToPersianDigits(rounded.ToString("#,##0", CultureInfo.InvariantCulture));
    }

    /// <summary>An amount of rials written for display: "۱,۲۵۰,۰۰۰ ریال".</summary>
    public static string FormatRials(decimal rials) => $"{FormatMoney(rials)} ریال";

    /// <summary>The same amount in tomans, with a toman fraction written out only when it is not whole.</summary>
    public static string FormatTomans(decimal rials)
    {
        var tomans = Math.Round(rials, 0, MidpointRounding.AwayFromZero) / 10m;
        var text = tomans == Math.Floor(tomans)
            ? tomans.ToString("#,##0", CultureInfo.InvariantCulture)
            : tomans.ToString("#,##0.0", CultureInfo.InvariantCulture);
        return $"{ToPersianDigits(text)} تومان";
    }

    private static string GroupWords(int group)
    {
        var parts = new List<string>(3);
        var hundreds = group / 100;
        var rest = group % 100;

        if (hundreds > 0)
        {
            parts.Add(Hundreds[hundreds]);
        }

        if (rest >= 20)
        {
            parts.Add(Tens[rest / 10]);
            if (rest % 10 > 0)
            {
                parts.Add(Ones[rest % 10]);
            }
        }
        else if (rest >= 10)
        {
            parts.Add(Teens[rest - 10]);
        }
        else if (rest > 0)
        {
            parts.Add(Ones[rest]);
        }

        // "یک" stays in front of a scale word ("یک هزار"), so a group of exactly one is not dropped.
        return string.Join(" و ", parts);
    }
}
