using ClosedXML.Excel;
using Nexus.ProjectManagement.Contracts.Application.Persian;

namespace Nexus.Reporting.Export.Application;

/// <summary>
/// Writes a <see cref="ReportDocument"/> as a spreadsheet: one right-to-left sheet per section. Numbers stay numbers (so they can be
/// summed and charted), dates are written as Jalali text, and a text cell is always plain text - never a formula, whatever it starts with.
/// </summary>
public static class XlsxRenderer
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const int MaxSheetNameLength = 31;
    private const int MinColumnWidth = 10;
    private const int MaxColumnWidth = 50;
    private static readonly char[] InvalidSheetChars = [':', '\\', '/', '?', '*', '[', ']'];

    public static byte[] Render(ReportDocument document)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = document.Title;

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in document.Sections)
        {
            WriteSection(workbook.Worksheets.Add(SheetName(section.Title ?? document.Title, usedNames)), document, section);
        }

        if (document.Sections.Count == 0)
        {
            var sheet = workbook.Worksheets.Add(SheetName(document.Title, usedNames));
            sheet.RightToLeft = true;
            WriteHeading(sheet, document, null);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSection(IXLWorksheet sheet, ReportDocument document, ReportSection section)
    {
        sheet.RightToLeft = true;
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);

        var row = WriteHeading(sheet, document, section.Title);
        var headerRow = row;

        for (var column = 0; column < section.Columns.Count; column++)
        {
            var cell = sheet.Cell(row, column + 1);
            cell.Value = section.Columns[column].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EDF3");
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        foreach (var values in section.Rows)
        {
            row++;
            for (var column = 0; column < section.Columns.Count; column++)
            {
                WriteCell(sheet.Cell(row, column + 1), section.Columns[column].Kind, column < values.Count ? values[column] : null);
            }
        }

        for (var column = 0; column < section.Columns.Count; column++)
        {
            var longest = section.Columns[column].Header.Length;
            foreach (var values in section.Rows)
            {
                longest = Math.Max(longest, column < values.Count ? TextLength(values[column]) : 0);
            }

            sheet.Column(column + 1).Width = Math.Clamp(longest + 2, MinColumnWidth, MaxColumnWidth);
        }

        if (section.Rows.Count > 0)
        {
            sheet.SheetView.FreezeRows(headerRow);
        }
    }

    /// <summary>Title, subtitle and section title above the table; returns the row the table's header goes on.</summary>
    private static int WriteHeading(IXLWorksheet sheet, ReportDocument document, string? sectionTitle)
    {
        var row = 1;
        sheet.Cell(row, 1).Value = document.Title;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        row++;

        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            sheet.Cell(row, 1).Value = document.Subtitle;
            row++;
        }

        if (!string.IsNullOrWhiteSpace(sectionTitle) && sectionTitle != document.Title)
        {
            sheet.Cell(row, 1).Value = sectionTitle;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            row++;
        }

        return row + 1;
    }

    private static void WriteCell(IXLCell cell, ColumnKind kind, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case DateOnly date:
                cell.Value = PersianDates.Format(date);
                return;
            case int or long or decimal or short or byte or double when kind != ColumnKind.Text:
                cell.Value = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                cell.Style.NumberFormat.Format = value is int or long or short or byte ? "#,##0" : "#,##0.##";
                return;
            default:
                // Stored as text and typed as text, so "=1+1" or "+98..." is shown as written and never evaluated.
                cell.Style.NumberFormat.Format = "@";
                cell.Value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                return;
        }
    }

    private static int TextLength(object? value) => value switch
    {
        null => 0,
        DateOnly => 10,
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Length ?? 0
    };

    /// <summary>Excel allows 31 characters and none of : \ / ? * [ ], and sheet names must differ.</summary>
    private static string SheetName(string wanted, HashSet<string> used)
    {
        var cleaned = new string(wanted.Where(c => !InvalidSheetChars.Contains(c)).ToArray()).Trim().Trim('\'');
        if (cleaned.Length == 0)
        {
            cleaned = "Sheet";
        }

        var name = cleaned.Length > MaxSheetNameLength ? cleaned[..MaxSheetNameLength] : cleaned;
        for (var attempt = 2; !used.Add(name); attempt++)
        {
            var suffix = $" {attempt}";
            name = (cleaned.Length + suffix.Length > MaxSheetNameLength ? cleaned[..(MaxSheetNameLength - suffix.Length)] : cleaned) + suffix;
        }

        return name;
    }
}
