using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Reads the structure sheet (phase 6.2) from an .xlsx (its first sheet) or a .csv (UTF-8) file,
/// without any library: columns code, name, parent code, type, manager's position, in this order.
/// The first row is the header. Each row keeps the sheet's own number (the header is row 1).
/// </summary>
public static class OrgSheetReader
{
    public const int MaxRows = 5000;

    /// <summary>The rows, or why the file could not be read.</summary>
    public static (IReadOnlyList<ExcelStructureRow> Rows, string? Problem) Read(Stream content, string? fileName)
    {
        try
        {
            var lines = fileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true ? Csv(content) : Xlsx(content);
            if (lines.Count - 1 > MaxRows)
            {
                return ([], $"فایل حداکثر {MaxRows} سطر می‌تواند داشته باشد.");
            }

            var rows = lines.Skip(1)
                .Select(line => new ExcelStructureRow(line.Row, Cell(line.Cells, 0), Cell(line.Cells, 1), Cell(line.Cells, 2), Cell(line.Cells, 3), Cell(line.Cells, 4)))
                .ToList();
            return (rows, null);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException or IOException or KeyNotFoundException)
        {
            return ([], "فایل خوانده نشد؛ فایل اکسل (xlsx) یا CSV با UTF-8 بفرستید.");
        }
    }

    private static string? Cell(IReadOnlyList<string?> cells, int index) =>
        index < cells.Count && !string.IsNullOrWhiteSpace(cells[index]) ? cells[index]!.Trim() : null;

    private sealed record Line(int Row, IReadOnlyList<string?> Cells);

    private static List<Line> Csv(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<Line>();
        var text = reader.ReadToEnd();
        var row = 1;
        var cells = new List<string?>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') { quoted = false; }
                else { cell.Append(c); }
                continue;
            }

            switch (c)
            {
                case '"': quoted = true; break;
                case ',': cells.Add(cell.ToString()); cell.Clear(); break;
                case '\r': break;
                case '\n':
                    cells.Add(cell.ToString()); cell.Clear();
                    lines.Add(new Line(row++, cells));
                    cells = [];
                    break;
                default: cell.Append(c); break;
            }
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            lines.Add(new Line(row, cells));
        }

        return lines;
    }

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static List<Line> Xlsx(Stream content)
    {
        using var zip = new ZipArchive(content, ZipArchiveMode.Read);
        XDocument Load(string path) =>
            XDocument.Load((zip.GetEntry(path) ?? throw new InvalidDataException(path)).Open());

        var shared = zip.GetEntry("xl/sharedStrings.xml") is null
            ? []
            : Load("xl/sharedStrings.xml").Root!.Elements(Main + "si").Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value))).ToList();

        // The first sheet of the workbook, wherever its file is.
        var firstSheet = Load("xl/workbook.xml").Root!.Element(Main + "sheets")!.Elements(Main + "sheet").First();
        var relId = (string)firstSheet.Attribute(Rel + "id")!;
        var target = (string)Load("xl/_rels/workbook.xml.rels").Root!.Elements(PackageRel + "Relationship")
            .First(r => (string?)r.Attribute("Id") == relId).Attribute("Target")!;
        var sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

        var lines = new List<Line>();
        foreach (var row in Load(sheetPath).Descendants(Main + "row"))
        {
            var number = int.Parse((string)row.Attribute("r")!, CultureInfo.InvariantCulture);
            var cells = new List<string?>();
            foreach (var c in row.Elements(Main + "c"))
            {
                var column = ColumnIndex((string)c.Attribute("r")!);
                while (cells.Count < column)
                {
                    cells.Add(null);
                }

                var type = (string?)c.Attribute("t");
                var value = c.Element(Main + "v")?.Value;
                cells.Add(type switch
                {
                    "s" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index < shared.Count => shared[index],
                    "inlineStr" => string.Concat(c.Descendants(Main + "t").Select(t => t.Value)),
                    _ => value,
                });
            }

            lines.Add(new Line(number, cells));
        }

        return lines;
    }

    /// <summary>"C12" → 2.</summary>
    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var ch in reference.TakeWhile(char.IsLetter))
        {
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return index - 1;
    }
}
