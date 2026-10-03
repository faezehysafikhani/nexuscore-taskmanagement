using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Nexus.CompositionTests;

/// <summary>
/// Checks an upgrade script and its code twin (the module's SchemaUpgrade helper) against the DDL EF
/// generates from the module's model, so a model change that forgets the upgrade fails a test:
/// every column of every new table, every index on them, every added column, and the order in
/// which the helper runs the script's statements.
/// </summary>
internal static class SchemaUpgradeVerifier
{
    public static string Normalise(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    public static string ReadScript(string fileName) =>
        File.ReadAllText(Path.Combine(FindSolutionRoot(), "docs", "upgrade", fileName));

    public static string[] ReadHelperStatements(Type helper) =>
        (string[])helper.GetField("Statements", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    /// <summary>The column lines of <c>CREATE TABLE [schema].[table]</c> as EF would write them.</summary>
    public static List<string> ColumnLines(string generatedDdl, string schema, string table)
    {
        var match = Regex.Match(generatedDdl, $@"CREATE TABLE \[{schema}\]\.\[{table}\] \((?<body>.*?)\r?\n\);", RegexOptions.Singleline);
        Assert.True(match.Success, $"EF does not generate a [{schema}].[{table}] table.");
        return match.Groups["body"].Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimEnd(','))
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <param name="newTables">Tables the upgrade creates.</param>
    /// <param name="addedColumns">Columns the upgrade adds to tables that already existed, as EF declares them ("[Rank] int NOT NULL").</param>
    public static void AssertMatchesModel(
        DbContext db, string schema, string scriptFile, Type helper,
        string[] newTables, params (string Table, string ColumnLine)[] addedColumns)
    {
        var generated = db.Database.GenerateCreateScript();
        var script = Normalise(ReadScript(scriptFile));
        var helperText = Normalise(string.Join("\n", ReadHelperStatements(helper)));

        foreach (var table in newTables)
        {
            var lines = ColumnLines(generated, schema, table);
            Assert.True(lines.Count > 3, table);
            foreach (var line in lines)
            {
                Assert.Contains(Normalise(line), script);
                Assert.Contains(Normalise(line), helperText);
            }
        }

        foreach (var (table, columnLine) in addedColumns)
        {
            Assert.Contains(columnLine, ColumnLines(generated, schema, table)); // EF really has this column
            var column = Regex.Match(columnLine, @"^\[(?<name>\w+)\]").Groups["name"].Value;
            foreach (var text in new[] { script, helperText })
            {
                Assert.Contains($"COL_LENGTH(N'{schema}.{table}', N'{column}') IS NULL", text);
                Assert.Contains($"ALTER TABLE [{schema}].[{table}] ADD {columnLine}", text);
            }
        }

        var indexes = Regex.Matches(generated, $@"CREATE (?<unique>UNIQUE )?INDEX \[(?<name>\w+)\] ON \[{schema}\]\.\[(?<table>\w+)\] \((?<columns>[^)]*)\);")
            .Where(m => newTables.Contains(m.Groups["table"].Value))
            .ToList();
        foreach (var index in indexes)
        {
            var expected = Normalise($"CREATE {index.Groups["unique"].Value}INDEX [{index.Groups["name"].Value}] ON [{schema}].[{index.Groups["table"].Value}] ({index.Groups["columns"].Value});");
            Assert.Contains(expected, script);
            Assert.Contains(expected, helperText);
        }

        AssertSameOrder(script, ReadHelperStatements(helper));
    }

    /// <summary>Every helper statement appears in the script, in the same sequence.</summary>
    public static void AssertSameOrder(string normalisedScript, IEnumerable<string> helperStatements)
    {
        var position = -1;
        foreach (var statement in helperStatements)
        {
            var text = Normalise(statement);
            var next = normalisedScript.IndexOf(text, position + 1, StringComparison.Ordinal);
            Assert.True(next > position, $"Out of order or missing in the script: {text[..Math.Min(90, text.Length)]}");
            position = next;
        }
    }

    /// <summary>The upgrade may add and create; it must never drop, delete or truncate. Updates are
    /// refused too unless the caller names the single column it is allowed to back-fill.</summary>
    public static void AssertAdditiveOnly(string scriptFile, Type helper, string? allowedUpdateColumn = null)
    {
        foreach (var text in new[] { ReadScript(scriptFile), string.Join("\n", ReadHelperStatements(helper)) })
        {
            var code = Regex.Replace(text, @"--[^\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            Assert.DoesNotMatch(@"(?i)\b(DROP|DELETE|TRUNCATE)\b", code);

            var updates = Regex.Matches(code, @"(?i)\bUPDATE\b[^;]*?\bSET\b(?<set>[^;]*?)\bFROM\b");
            if (allowedUpdateColumn is null)
            {
                Assert.DoesNotMatch(@"(?i)\bUPDATE\b", code);
            }
            else
            {
                Assert.Equal(1, updates.Count);
                Assert.Matches($@"^\s*\w+\.\[{allowedUpdateColumn}\]\s*=", updates[0].Groups["set"].Value);
            }
        }
    }

    public static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexusCore.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("NexusCore.sln not found above the test output.");
    }
}
