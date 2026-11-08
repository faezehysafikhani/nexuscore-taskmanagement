using Microsoft.EntityFrameworkCore;
using Nexus.Integrations.StrategyAlignment.Application;
using Nexus.Integrations.StrategyAlignment.Application.Dtos;
using Nexus.Integrations.StrategyAlignment.Domain;
using Nexus.Integrations.StrategyAlignment.Infrastructure;
using Nexus.Knowledge.Domain;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.StrategyManagement.Application;
using Nexus.StrategyManagement.Application.Dtos;
using Nexus.StrategyManagement.Infrastructure;

namespace Nexus.CompositionTests;

/// <summary>The strategy tree (effective weights, save-all with the 100 rule) and the alignment scores the screens used to work out themselves.</summary>
public sealed class StrategyTreeTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class FakeProjects : IProjectRepository
    {
        public List<Project> Items { get; } = [];
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(p => p.Id == id));
        public Task<NexusCore.SharedKernel.Results.PagedResult<Project>> ListAsync(ListProjectsRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(Project project, CancellationToken ct) { Items.Add(project); return Task.CompletedTask; }
    }

    private sealed class Fixture
    {
        public StrategyManagementDbContext StrategyDb { get; } = new(new DbContextOptionsBuilder<StrategyManagementDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public StrategyAlignmentDbContext AlignmentDb { get; } = new(new DbContextOptionsBuilder<StrategyAlignmentDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public FakeProjects Projects { get; } = new();
        public StrategyService Strategies { get; }
        public AlignmentService Alignment { get; }

        public Fixture()
        {
            var repository = new StrategyRepository(StrategyDb);
            Strategies = new StrategyService(repository, StrategyDb);
            Alignment = new AlignmentService(new AlignmentRepository(AlignmentDb), Projects, repository, AlignmentDb);
        }

        public Guid AddProject()
        {
            var project = new Project(Guid.NewGuid(), Tenant, "Project", "P" + Guid.NewGuid().ToString("N")[..6], ProjectType.Waterfall);
            Projects.Items.Add(project);
            return project.Id;
        }
    }

    private static SaveStrategyTreeItem Item(string key, string name, decimal weight, string? parent = null) => new(key, name, null, weight, parent);

    /// <summary>Quality 60 (Speed 20 + Cost 40) and Growth 40.</summary>
    private static SaveStrategyTreeRequest Sample() => new(Tenant,
    [
        Item("q", "Quality", 0),
        Item("speed", "Speed", 20, "q"),
        Item("cost", "Cost", 40, "q"),
        Item("g", "Growth", 40)
    ]);

    // ------------------------------------------------------------------ tree

    [Fact]
    public async Task SavingATree_StoresIt_AndTheTreeReportsLevelsLeavesAndEffectiveWeights()
    {
        var f = new Fixture();

        var saved = (await f.Strategies.SaveTreeAsync(Sample(), default)).Value!;
        var read = (await f.Strategies.GetTreeAsync(Tenant, default)).Value!;

        Assert.Equal(100, saved.TotalWeight);
        Assert.Equal(["Quality", "Speed", "Cost", "Growth"], read.Nodes.Select(n => n.Name)); // a parent before its children, in the saved order
        var quality = read.Nodes.Single(n => n.Name == "Quality");
        Assert.Equal((0, false, 0m, 60m), (quality.Level, quality.IsLeaf, quality.StoredWeight, quality.EffectiveWeight));
        var cost = read.Nodes.Single(n => n.Name == "Cost");
        Assert.Equal((1, true, 40m, quality.Id), (cost.Level, cost.IsLeaf, cost.EffectiveWeight, cost.ParentStrategyId));
    }

    [Fact]
    public async Task SavingAgain_UpdatesByIdKeepsOrder_CreatesNewOnes_AndDeletesTheOnesLeftOut()
    {
        var f = new Fixture();
        var first = (await f.Strategies.SaveTreeAsync(Sample(), default)).Value!;
        var quality = first.Nodes.Single(n => n.Name == "Quality").Id.ToString();
        var growth = first.Nodes.Single(n => n.Name == "Growth").Id.ToString();

        var second = (await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant,
        [
            Item("new-1", "People", 25),
            Item(growth, "Growth (renamed)", 75)
        ]), default)).Value!;

        Assert.Equal(["People", "Growth (renamed)"], second.Nodes.Select(n => n.Name));
        Assert.Equal(Guid.Parse(growth), second.Nodes[1].Id); // the same row, renamed
        Assert.DoesNotContain(second.Nodes, n => n.Id.ToString() == quality);
        Assert.Equal(2, await f.StrategyDb.Strategies.CountAsync());
    }

    [Fact]
    public async Task TheTopLevelWeights_MustAddUpTo100_ButAnEmptyTreeIsFine_AndNothingIsSavedOnARefusal()
    {
        var f = new Fixture();
        await f.Strategies.SaveTreeAsync(Sample(), default);

        var bad = await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, [Item("a", "A", 30), Item("b", "B", 30)]), default);

        Assert.Equal(StrategyService.WeightsNot100ErrorCode, bad.Error.Code);
        Assert.Equal(4, await f.StrategyDb.Strategies.CountAsync()); // untouched

        Assert.True((await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, []), default)).IsSuccess);
        Assert.Equal(0, await f.StrategyDb.Strategies.CountAsync());
    }

    [Fact]
    public async Task AWeightThatRoundsToTheHundred_IsAccepted()
    {
        var f = new Fixture();

        var result = await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, [Item("a", "A", 33.4m), Item("b", "B", 33.3m), Item("c", "C", 33.3m)]), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ABrokenTreeIsRefused()
    {
        var f = new Fixture();

        Assert.Equal("validation.error", (await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, [Item("a", " ", 100)]), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, [Item("a", "A", 50), Item("a", "A2", 50)]), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, [Item("a", "A", 100, "nope")]), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, [Item("a", "A", 100, "a")]), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, [Item("a", "A", 50, "b"), Item("b", "B", 50, "a")]), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, [Item("a", "A", 101)]), default)).Error.Code);
        Assert.Equal(0, await f.StrategyDb.Strategies.CountAsync());
    }

    [Fact]
    public async Task TheOldListEndpoint_KeepsWorking_AndOrdersByNameWhenNothingWasArranged()
    {
        var f = new Fixture();
        await f.Strategies.CreateAsync(new CreateStrategyRequest(Tenant, "Zeta", null, 10, null), default);
        await f.Strategies.CreateAsync(new CreateStrategyRequest(Tenant, "Alpha", null, 10, null), default);

        var list = (await f.Strategies.ListAsync(Tenant, default)).Value!;

        Assert.Equal(["Alpha", "Zeta"], list.Select(s => s.Name));
    }

    // ------------------------------------------------------------------ alignment

    [Fact]
    public async Task TheScore_WeighsEachLeafLevelByItsWeight_AsAShareOfTheBestPossible()
    {
        var f = new Fixture();
        var tree = (await f.Strategies.SaveTreeAsync(Sample(), default)).Value!;
        var speed = tree.Nodes.Single(n => n.Name == "Speed").Id;
        var cost = tree.Nodes.Single(n => n.Name == "Cost").Id;
        var growth = tree.Nodes.Single(n => n.Name == "Growth").Id;
        var project = f.AddProject();

        var score = (await f.Alignment.SetLevelsAsync(new SetProjectAlignmentRequest(Tenant, project,
        [
            new StrategyLevelDto(speed, AlignmentLevel.High),   // 3 * 20 = 60
            new StrategyLevelDto(cost, AlignmentLevel.Low),     // 1 * 40 = 40
            new StrategyLevelDto(growth, AlignmentLevel.Medium) // 2 * 40 = 80
        ]), default)).Value!;

        // 180 of the best 3 * 100 = 300.
        Assert.Equal(60m, score.Percentage);
        Assert.Equal(3, score.Levels.Count);
        Assert.Equal(AlignmentLevel.High, score.Levels.Single(l => l.StrategyId == speed).Level);
    }

    [Fact]
    public async Task AStrategyWithNoRow_CountsAsNone_AndTheScoresComeBackForSeveralProjectsAtOnce()
    {
        var f = new Fixture();
        var tree = (await f.Strategies.SaveTreeAsync(Sample(), default)).Value!;
        var growth = tree.Nodes.Single(n => n.Name == "Growth").Id;
        var aligned = f.AddProject();
        var blank = f.AddProject();
        await f.Alignment.SetLevelsAsync(new SetProjectAlignmentRequest(Tenant, aligned, [new StrategyLevelDto(growth, AlignmentLevel.High)]), default);

        var scores = (await f.Alignment.GetScoresAsync(Tenant, new ProjectAlignmentScoresRequest([aligned, blank]), default)).Value!;

        Assert.Equal(40m, scores.Single(s => s.ProjectId == aligned).Percentage); // 3 * 40 of 3 * 100
        var empty = scores.Single(s => s.ProjectId == blank);
        Assert.Equal(0m, empty.Percentage);
        Assert.All(empty.Levels, l => Assert.Equal(AlignmentLevel.None, l.Level));
        Assert.Equal(3, empty.Levels.Count); // only the leaves
    }

    [Fact]
    public async Task SettingLevels_UpdatesTheSameRows_RefusesAParentStrategy_AndAnUnknownProject()
    {
        var f = new Fixture();
        var tree = (await f.Strategies.SaveTreeAsync(Sample(), default)).Value!;
        var growth = tree.Nodes.Single(n => n.Name == "Growth").Id;
        var quality = tree.Nodes.Single(n => n.Name == "Quality").Id;
        var project = f.AddProject();

        await f.Alignment.SetLevelsAsync(new SetProjectAlignmentRequest(Tenant, project, [new StrategyLevelDto(growth, AlignmentLevel.Low)]), default);
        await f.Alignment.SetLevelsAsync(new SetProjectAlignmentRequest(Tenant, project, [new StrategyLevelDto(growth, AlignmentLevel.High)]), default);

        Assert.Equal(1, await f.AlignmentDb.ProjectStrategyAlignments.CountAsync());
        Assert.Equal(AlignmentLevel.High, (await f.AlignmentDb.ProjectStrategyAlignments.SingleAsync()).AlignmentLevel);
        Assert.Equal("validation.error", (await f.Alignment.SetLevelsAsync(new SetProjectAlignmentRequest(Tenant, project, [new StrategyLevelDto(quality, AlignmentLevel.High)]), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Alignment.SetLevelsAsync(new SetProjectAlignmentRequest(Tenant, Guid.NewGuid(), []), default)).Error.Code);
        Assert.Equal("validation.error", (await f.Alignment.SetLevelsAsync(new SetProjectAlignmentRequest(Tenant, project, [new StrategyLevelDto(growth, (AlignmentLevel)9)]), default)).Error.Code);
    }

    [Fact]
    public async Task ARowLeftBehindByADeletedStrategy_IsIgnoredByTheScore()
    {
        var f = new Fixture();
        var tree = (await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, [Item("a", "A", 50), Item("b", "B", 50)]), default)).Value!;
        var a = tree.Nodes.Single(n => n.Name == "A").Id;
        var b = tree.Nodes.Single(n => n.Name == "B").Id;
        var project = f.AddProject();
        await f.Alignment.SetLevelsAsync(new SetProjectAlignmentRequest(Tenant, project, [new StrategyLevelDto(a, AlignmentLevel.High), new StrategyLevelDto(b, AlignmentLevel.High)]), default);

        await f.Strategies.SaveTreeAsync(new SaveStrategyTreeRequest(Tenant, [Item(b.ToString(), "B", 100)]), default);
        var score = (await f.Alignment.GetScoresAsync(Tenant, new ProjectAlignmentScoresRequest([project]), default)).Value!.Single();

        Assert.Equal(100m, score.Percentage);
        Assert.Single(score.Levels);
    }

    [Fact]
    public void KnowledgePamphlet_IsAppended_SoTheStoredTypesKeepTheirMeaning()
    {
        Assert.Equal(0, (int)KnowledgeDocumentType.Book);
        Assert.Equal(1, (int)KnowledgeDocumentType.Software);
        Assert.Equal(2, (int)KnowledgeDocumentType.Notes);
        Assert.Equal(3, (int)KnowledgeDocumentType.Other);
        Assert.Equal(4, (int)KnowledgeDocumentType.Pamphlet);
    }

    [Fact]
    public void TheStrategyUpgradeScriptAndHelper_MatchTheModel_AndOnlyAdd()
    {
        using var db = new StrategyManagementDbContext(
            new DbContextOptionsBuilder<StrategyManagementDbContext>()
                .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True").Options);

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "strategy", "2026-10-14-add-strategy-sort-order.sql", typeof(StrategySchemaUpgrade), [], ("Strategies", "[SortOrder] int NOT NULL"));
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-14-add-strategy-sort-order.sql", typeof(StrategySchemaUpgrade));
    }
}
