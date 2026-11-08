using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Agile.Domain;
using Nexus.ProjectManagement.Agile.Infrastructure;

namespace Nexus.CompositionTests;

public sealed class AgileSchemaTests
{
    private const string ScriptFile = "2026-10-03-add-agile-sprints.sql";

    private static AgileDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<AgileDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "agile_planning", ScriptFile, typeof(AgileSchemaUpgrade),
            ["Sprints", "SprintEvents", "ChecklistItems"],
            ("AgileTasks", "[StoryPoints] int NULL"));
    }

    [Fact]
    public void TheRankColumn_IsAddedWithADefault_ThenNumberedOnlyWhileNoTaskHasARank()
    {
        using var db = NewSqlServerContext();
        var generated = db.Database.GenerateCreateScript();
        Assert.Contains("[Rank] int NOT NULL", SchemaUpgradeVerifier.ColumnLines(generated, "agile_planning", "AgileTasks"));

        foreach (var text in new[] { SchemaUpgradeVerifier.Normalise(SchemaUpgradeVerifier.ReadScript(ScriptFile)),
                     SchemaUpgradeVerifier.Normalise(string.Join("\n", SchemaUpgradeVerifier.ReadHelperStatements(typeof(AgileSchemaUpgrade)))) })
        {
            Assert.Contains("COL_LENGTH(N'agile_planning.AgileTasks', N'Rank') IS NULL", text);
            Assert.Contains("ADD [Rank] int NOT NULL CONSTRAINT [DF_AgileTasks_Rank] DEFAULT 0", text);
            // The numbering is guarded, so a later run cannot undo a reordering.
            Assert.Contains("IF NOT EXISTS (SELECT 1 FROM [agile_planning].[AgileTasks] WHERE [Rank] <> 0)", text);
            Assert.Contains("ROW_NUMBER() OVER (PARTITION BY [ProjectId], [Status] ORDER BY [CreatedAtUtc], [Id]) - 1", text);
        }
    }

    [Fact]
    public void TheUpgrade_OnlyAddsAndCreates_ExceptForTheOneRankBackfill()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly(ScriptFile, typeof(AgileSchemaUpgrade), allowedUpdateColumn: "Rank");
    }

    [Fact]
    public void TheModel_KeepsSprintNumbersUniquePerProject_AndHasNoDatabaseDefaultsOnTheNewColumns()
    {
        using var db = NewSqlServerContext();

        var unique = db.Model.FindEntityType(typeof(Sprint))!.GetIndexes().Where(i => i.IsUnique).ToList();
        Assert.Equal(["ProjectId+Number"], unique.Select(i => string.Join("+", i.Properties.Select(p => p.Name))));

        // EF writes Rank itself; the DEFAULT 0 exists only to give pre-existing rows a value.
        var rank = db.Model.FindEntityType(typeof(AgileTask))!.FindProperty(nameof(AgileTask.Rank))!;
        Assert.Null(rank.FindAnnotation("Relational:DefaultValue"));
    }

    [Fact]
    public void TheStatusAndEventEnums_KeepTheirStoredValues()
    {
        // These are stored as ints by the script's comments and by existing rows: they must never move.
        Assert.Equal([0, 1, 2], new[] { (int)SprintStatus.Planned, (int)SprintStatus.Active, (int)SprintStatus.Completed });
        Assert.Equal(
            [0, 1, 2, 3, 4],
            new[] { SprintEventType.ScopeAdded, SprintEventType.ScopeRemoved, SprintEventType.Completed, SprintEventType.Reopened, SprintEventType.CarriedOver }.Select(e => (int)e));
        Assert.Equal([0, 1, 2, 3], new[] { AgileTaskStatus.ToDo, AgileTaskStatus.InProgress, AgileTaskStatus.Done, AgileTaskStatus.UnderReview }.Select(e => (int)e));
    }
}
