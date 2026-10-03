using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Nexus.ProjectManagement.Waterfall.Domain;
using Nexus.ProjectManagement.Waterfall.Infrastructure;

namespace Nexus.CompositionTests;

public sealed class WaterfallSchemaTests
{
    private static readonly string[] NewTables =
        ["ActivityDependencies", "ScheduleBaselines", "ScheduleBaselineActivities", "ProgressSnapshots"];

    private static WaterfallDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    private static string Normalise(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string Script() =>
        Normalise(File.ReadAllText(Path.Combine(FindSolutionRoot(), "docs", "upgrade", "2026-10-03-add-waterfall-scheduling.sql")));

    private static string Helper() =>
        Normalise(string.Join("\n", (string[])typeof(WaterfallSchemaUpgrade)
            .GetField("Statements", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!));

    private static List<string> ColumnLines(string generatedDdl, string table)
    {
        var match = Regex.Match(generatedDdl, $@"CREATE TABLE \[waterfall\]\.\[{table}\] \((?<body>.*?)\r?\n\);", RegexOptions.Singleline);
        Assert.True(match.Success, $"EF no longer generates the {table} table the upgrade script assumes.");
        return match.Groups["body"].Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimEnd(','))
            .Where(line => line.Length > 0)
            .ToList();
    }

    [Fact]
    public void TheUpgradeScriptAndHelper_CreateExactlyTheTablesTheModelDescribes()
    {
        using var db = NewSqlServerContext();
        var generated = db.Database.GenerateCreateScript();
        var script = Script();
        var helper = Helper();

        foreach (var table in NewTables)
        {
            var lines = ColumnLines(generated, table);
            Assert.True(lines.Count > 3, table);
            foreach (var line in lines)
            {
                Assert.Contains(Normalise(line), script);
                Assert.Contains(Normalise(line), helper);
            }

            Assert.Contains($"[waterfall].[{table}]", script);
            Assert.Contains($"[waterfall].[{table}]", helper);
        }
    }

    [Fact]
    public void TheUpgrade_CreatesEveryIndexTheModelHas_OnTheNewTables()
    {
        using var db = NewSqlServerContext();
        var generated = db.Database.GenerateCreateScript();
        var script = Script();
        var helper = Helper();

        var indexes = Regex.Matches(generated, @"CREATE (?<unique>UNIQUE )?INDEX \[(?<name>\w+)\] ON \[waterfall\]\.\[(?<table>\w+)\] \((?<columns>[^)]*)\);")
            .Where(m => NewTables.Contains(m.Groups["table"].Value))
            .ToList();
        Assert.Equal(6, indexes.Count); // 3 + 1 + 1 + 1: dependencies, baselines, baseline activities, snapshots

        foreach (var index in indexes)
        {
            var expected = Normalise($"CREATE {index.Groups["unique"].Value}INDEX [{index.Groups["name"].Value}] ON [waterfall].[{index.Groups["table"].Value}] ({index.Groups["columns"].Value});");
            Assert.Contains(expected, script);
            Assert.Contains(expected, helper);
        }
    }

    [Fact]
    public void TheUniqueIndexes_EnforceTheRulesTheServicesRelyOn()
    {
        using var db = NewSqlServerContext();

        string[] UniqueIndexColumns(Type entity) => db.Model.FindEntityType(entity)!.GetIndexes()
            .Where(i => i.IsUnique).Select(i => string.Join("+", i.Properties.Select(p => p.Name))).ToArray();

        Assert.Equal(["PredecessorActivityId+SuccessorActivityId"], UniqueIndexColumns(typeof(ActivityDependency)));
        Assert.Equal(["ProjectId+Number"], UniqueIndexColumns(typeof(ScheduleBaseline)));
        Assert.Equal(["ProjectId+SnapshotDate"], UniqueIndexColumns(typeof(ProgressSnapshot)));
    }

    [Fact]
    public void IsMilestone_IsANonNullBit_AddedWithDefaultFalse_ToExistingActivities()
    {
        using var db = NewSqlServerContext();
        var property = db.Model.FindEntityType(typeof(Activity))!.FindProperty(nameof(Activity.IsMilestone))!;

        Assert.Equal("bit", property.GetColumnType());
        Assert.False(property.IsNullable);
        // EF itself writes the value; the DEFAULT exists only to give rows that predate the column a value.
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));

        var generated = db.Database.GenerateCreateScript();
        Assert.Contains("[IsMilestone] bit NOT NULL", ColumnLines(generated, "Activities"));

        foreach (var text in new[] { Script(), Helper() })
        {
            Assert.Contains(Normalise("ALTER TABLE [waterfall].[Activities] ADD [IsMilestone] bit NOT NULL CONSTRAINT [DF_Activities_IsMilestone] DEFAULT CAST(0 AS bit)"), text);
            Assert.Contains("COL_LENGTH(N'waterfall.Activities', N'IsMilestone') IS NULL", text);
        }
    }

    [Fact]
    public void TheUpgrade_OnlyEverAddsOrCreates_NeverDropsOrRewritesData()
    {
        foreach (var text in new[] { Script(), Helper() })
        {
            Assert.DoesNotMatch(@"(?i)\b(DROP|DELETE|TRUNCATE|UPDATE)\b", Regex.Replace(text, @"--[^\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline));
        }
    }

    [Fact]
    public void TheHelper_RunsTheScriptsStatementsInTheSameOrder()
    {
        var script = Script();
        var statements = (string[])typeof(WaterfallSchemaUpgrade).GetField("Statements", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        // Every statement of the helper appears in the script, and in the same sequence.
        var position = -1;
        foreach (var statement in statements)
        {
            var next = script.IndexOf(Normalise(statement), position + 1, StringComparison.Ordinal);
            Assert.True(next > position, $"Out of order or missing: {Normalise(statement)[..Math.Min(80, Normalise(statement).Length)]}");
            position = next;
        }

        Assert.Equal(12, statements.Length);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexusCore.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("NexusCore.sln not found above the test output.");
    }
}
