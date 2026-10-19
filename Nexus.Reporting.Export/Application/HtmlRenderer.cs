using System.Globalization;
using System.Net;
using System.Text;
using Nexus.ProjectManagement.Contracts.Application.Persian;

namespace Nexus.Reporting.Export.Application;

/// <summary>
/// Writes a <see cref="ReportDocument"/> as a self-contained, right-to-left page set up for printing: the browser's "Print" then
/// "Save as PDF" gives the PDF, with the table headings repeated on every page. No external file, font or script is loaded (a bank network
/// has no CDN); every piece of text is HTML-encoded; numbers and dates are shown with Persian digits and Jalali dates.
/// </summary>
public static class HtmlRenderer
{
    public const string ContentType = "text/html; charset=utf-8";

    /// <summary>The page loads nothing from outside and only runs its own print button.</summary>
    public const string ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; base-uri 'none'; form-action 'none'";

    private const string Style = """
        :root { color-scheme: light; }
        * { box-sizing: border-box; }
        body { font-family: Vazirmatn, "IRANSans", Tahoma, "Segoe UI", sans-serif; margin: 24px; color: #1b2430; background: #fff; }
        h1 { font-size: 20px; margin: 0 0 4px; }
        h2 { font-size: 15px; margin: 24px 0 8px; }
        .subtitle { color: #5a6676; margin: 0 0 16px; font-size: 13px; }
        table { border-collapse: collapse; width: 100%; font-size: 12px; }
        th, td { border: 1px solid #c9d1dc; padding: 5px 8px; text-align: right; vertical-align: top; }
        th { background: #e8edf3; font-weight: 700; }
        td.num { text-align: left; direction: ltr; unicode-bidi: plaintext; white-space: nowrap; }
        tbody tr:nth-child(even) { background: #f6f8fb; }
        .empty { color: #5a6676; font-size: 12px; }
        .actions { margin-bottom: 16px; }
        button { font: inherit; padding: 6px 14px; cursor: pointer; }
        @page { size: A4 landscape; margin: 12mm; }
        @media print {
            body { margin: 0; }
            .actions { display: none; }
            thead { display: table-header-group; }
            tr { break-inside: avoid; }
            h2 { break-after: avoid; }
        }
        """;

    public static string Render(ReportDocument document)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n<html lang=\"fa\" dir=\"rtl\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(Encode(document.Title)).Append("</title>\n<style>").Append(Style).Append("</style>\n</head>\n<body>\n");
        html.Append("<div class=\"actions\"><button type=\"button\" onclick=\"window.print()\">چاپ / ذخیره به‌صورت PDF</button></div>\n");
        html.Append("<h1>").Append(Encode(document.Title)).Append("</h1>\n");
        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            html.Append("<p class=\"subtitle\">").Append(Encode(document.Subtitle)).Append("</p>\n");
        }

        foreach (var section in document.Sections)
        {
            WriteSection(html, section);
        }

        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    private static void WriteSection(StringBuilder html, ReportSection section)
    {
        if (!string.IsNullOrWhiteSpace(section.Title))
        {
            html.Append("<h2>").Append(Encode(section.Title)).Append("</h2>\n");
        }

        if (section.Rows.Count == 0)
        {
            html.Append("<p class=\"empty\">موردی برای نمایش وجود ندارد.</p>\n");
            return;
        }

        html.Append("<table>\n<thead><tr>");
        foreach (var column in section.Columns)
        {
            html.Append("<th>").Append(Encode(column.Header)).Append("</th>");
        }

        html.Append("</tr></thead>\n<tbody>\n");
        foreach (var values in section.Rows)
        {
            html.Append("<tr>");
            for (var column = 0; column < section.Columns.Count; column++)
            {
                var value = column < values.Count ? values[column] : null;
                var numeric = value is int or long or decimal or short or byte or double && section.Columns[column].Kind != ColumnKind.Text;
                html.Append(numeric ? "<td class=\"num\">" : "<td>").Append(Encode(Format(value))).Append("</td>");
            }

            html.Append("</tr>\n");
        }

        html.Append("</tbody>\n</table>\n");
    }

    /// <summary>A number with thousands separators and at most two decimals, a date as a Jalali date, all in Persian digits.</summary>
    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateOnly date => PersianDates.Format(date),
        int or long or short or byte => PersianNumbers.ToPersianDigits(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString("#,##0", CultureInfo.InvariantCulture)).Replace(',', '٬'),
        decimal or double => PersianNumbers.ToPersianDigits(Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("#,##0.##", CultureInfo.InvariantCulture)).Replace(',', '٬').Replace('.', '٫'),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
