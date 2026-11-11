using System.Globalization;
using System.Text;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Import of tasks from a CSV with a preview first (CAT-265). Columns (the first line names them, any order, comma / semicolon / tab):
/// title (required), dueDate (yyyy-MM-dd; Persian digits are fine), priority (Low / Medium / High or کم / متوسط / زیاد), description,
/// assignee (a sign-in name), tags (separated by ;). Every row goes through the same rules as a task made by hand.
/// </summary>
public sealed class TaskImportService(ITaskService tasks, ITaskRepository repository, ICurrentUserContext currentUser) : ITaskImportService
{
    public const int MaxRows = 500;
    public const int MaxChars = 2_000_000;

    private sealed record Parsed(int Line, string? Title, string? Description, DateOnly? Due, TaskPriority? Priority, string? Assignee, IReadOnlyList<string> Tags, List<string> Errors);

    public async Task<Result<ImportPreviewDto>> PreviewAsync(ImportTasksRequest request, CancellationToken cancellationToken)
    {
        var parsed = await ParseAsync(request.Csv, cancellationToken);
        if (parsed.IsFailure)
        {
            return Result.Failure<ImportPreviewDto>(parsed.Error);
        }

        var rows = parsed.Value.Select(ToDto).ToList();
        return Result.Success(new ImportPreviewDto(rows.Count, rows.Count(r => r.Valid), rows.Count(r => !r.Valid), rows));
    }

    public async Task<Result<ImportResultDto>> ImportAsync(ImportTasksRequest request, CancellationToken cancellationToken)
    {
        var parsed = await ParseAsync(request.Csv, cancellationToken);
        if (parsed.IsFailure)
        {
            return Result.Failure<ImportResultDto>(parsed.Error);
        }

        var bad = parsed.Value.Where(r => r.Errors.Count > 0).ToList();
        if (bad.Count > 0 && !request.SkipInvalid)
        {
            return Result.Failure<ImportResultDto>(Error.Validation($"{bad.Count} row(s) have errors (first: line {bad[0].Line}: {bad[0].Errors[0]}). Fix them or import with skipInvalid."));
        }

        var ids = new List<Guid>();
        var failed = bad.Select(ToDto).ToList();
        var users = await repository.GetUserIdsByUsernameAsync(
            currentUser.TenantId ?? Guid.Empty, parsed.Value.Where(r => r.Assignee is not null).Select(r => r.Assignee!).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken);
        foreach (var row in parsed.Value.Where(r => r.Errors.Count == 0))
        {
            var created = await tasks.CreateAsync(
                new CreateTaskRequest(
                    row.Title!, row.Due!.Value, row.Priority ?? TaskPriority.Medium, row.Description,
                    AssignedUserId: row.Assignee is not null ? users[row.Assignee] : currentUser.UserId,
                    Tags: row.Tags.Count == 0 ? null : row.Tags),
                cancellationToken);
            if (created.IsSuccess)
            {
                ids.Add(created.Value.Id);
            }
            else
            {
                // The task rules may refuse one (a holiday, a person out of reach): the rest still go.
                row.Errors.Add(created.Error.Message);
                failed.Add(ToDto(row));
            }
        }

        return Result.Success(new ImportResultDto(ids.Count, failed.Count, ids, failed.OrderBy(f => f.Line).ToList()));
    }

    private static ImportRowDto ToDto(Parsed r) => new(r.Line, r.Title, r.Due, r.Priority, r.Assignee, r.Errors.Count == 0, r.Errors.ToList());

    private async Task<Result<List<Parsed>>> ParseAsync(string? csv, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<List<Parsed>>(Error.Unauthorized());
        }

        if (string.IsNullOrWhiteSpace(csv) || csv.Length > MaxChars)
        {
            return Result.Failure<List<Parsed>>(Error.Validation("Send the CSV text (up to 2 million characters)."));
        }

        var table = ReadCsv(csv.TrimStart('\uFEFF'));
        if (table.Count < 2)
        {
            return Result.Failure<List<Parsed>>(Error.Validation("The CSV needs a header line and at least one row."));
        }

        if (table.Count - 1 > MaxRows)
        {
            return Result.Failure<List<Parsed>>(Error.Validation($"At most {MaxRows} rows at a time."));
        }

        var header = table[0].Select((name, i) => (Name: name.Trim().ToLowerInvariant(), i)).ToDictionary(x => x.Name, x => x.i);
        int? Col(params string[] names) => names.Select(n => header.TryGetValue(n, out var i) ? (int?)i : null).FirstOrDefault(i => i is not null);
        var title = Col("title", "عنوان");
        var due = Col("duedate", "due", "سررسید");
        if (title is null || due is null)
        {
            return Result.Failure<List<Parsed>>(Error.Validation("The header must have at least the columns title and dueDate."));
        }

        var priority = Col("priority", "اولویت");
        var description = Col("description", "توضیحات");
        var assignee = Col("assignee", "مسئول");
        var tags = Col("tags", "برچسب");
        string? Cell(List<string> row, int? col) => col is { } c && c < row.Count && !string.IsNullOrWhiteSpace(row[c]) ? row[c].Trim() : null;

        var rows = new List<Parsed>();
        for (var i = 1; i < table.Count; i++)
        {
            var cells = table[i];
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var errors = new List<string>();
            var t = Cell(cells, title);
            if (t is null) errors.Add("title is empty");
            else if (t.Length > 200) errors.Add("title is longer than 200 characters");

            var dueText = Normalize(Cell(cells, due));
            DateOnly? dueDate = null;
            if (dueText is null) errors.Add("dueDate is empty");
            else if (DateOnly.TryParseExact(dueText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) dueDate = d;
            else errors.Add($"dueDate '{dueText}' is not yyyy-MM-dd");

            TaskPriority? prio = null;
            var prioText = Cell(cells, priority);
            if (prioText is not null)
            {
                prio = prioText.ToLowerInvariant() switch
                {
                    "low" or "کم" => TaskPriority.Low,
                    "medium" or "متوسط" => TaskPriority.Medium,
                    "high" or "زیاد" => TaskPriority.High,
                    _ => null
                };
                if (prio is null) errors.Add($"priority '{prioText}' is not Low, Medium or High");
            }

            rows.Add(new Parsed(i + 1, t, Cell(cells, description), dueDate, prio, Cell(cells, assignee),
                (Cell(cells, tags)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []).Take(20).ToList(), errors));
        }

        // People named by a sign-in name must exist in the organization.
        var named = rows.Where(r => r.Assignee is not null).Select(r => r.Assignee!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = await repository.GetUserIdsByUsernameAsync(tenantId, named, cancellationToken);
        foreach (var r in rows.Where(r => r.Assignee is not null && !known.ContainsKey(r.Assignee!)))
        {
            r.Errors.Add($"assignee '{r.Assignee}' is not a user of this organization");
        }

        return Result.Success(rows);
    }

    private static string? Normalize(string? value)
    {
        if (value is null) return null;
        var b = new StringBuilder();
        foreach (var ch in value)
        {
            if (ch is >= '۰' and <= '۹') b.Append((char)('0' + (ch - '۰')));
            else if (ch is >= '٠' and <= '٩') b.Append((char)('0' + (ch - '٠')));
            else b.Append(ch);
        }

        return b.ToString();
    }

    /// <summary>RFC 4180: quoted cells may hold the delimiter, quotes ("") and line breaks. The delimiter is the commonest of , ; tab in the header line.</summary>
    internal static List<List<string>> ReadCsv(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var delimiter = new[] { ',', ';', '\t' }.OrderByDescending(c => firstLine.Count(x => x == c)).First();
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == delimiter) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                rows.Add(row); row = [];
            }
            else cell.Append(c);
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
