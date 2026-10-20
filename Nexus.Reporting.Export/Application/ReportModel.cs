namespace Nexus.Reporting.Export.Application;

/// <summary>How a column's cells are written. A number stays a number in a spreadsheet; a date is shown as a Jalali date.</summary>
public enum ColumnKind
{
    Text,
    Number,
    Date
}

public sealed record ReportColumn(string Header, ColumnKind Kind = ColumnKind.Text);

/// <summary>A table. Cells are a string, a number (int / long / decimal), a <see cref="DateOnly"/> or null (empty), matching the column's kind.</summary>
public sealed record ReportSection(string? Title, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>What a report says, independent of the file it is written to: the same document becomes a spreadsheet or a printable page.</summary>
public sealed record ReportDocument(string Title, string? Subtitle, IReadOnlyList<ReportSection> Sections);
