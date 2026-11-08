using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.StakeholderManagement.Application;
using Nexus.ProjectManagement.StakeholderManagement.Application.Dtos;
using Nexus.ProjectManagement.StakeholderManagement.Domain;
using Nexus.ProjectManagement.StakeholderManagement.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

public sealed class StakeholderRegisterTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class NoApproval : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) =>
            Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private static StakeholderService Build(bool withSettings = true)
    {
        var db = new StakeholderManagementDbContext(new DbContextOptionsBuilder<StakeholderManagementDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new StakeholderService(new StakeholderRepository(db), db, new NoApproval(), withSettings ? new StakeholderSettingsRepository(db) : null);
    }

    private static CreateStakeholderRequest New(Guid project, string name, PowerLevel power, InterestLevel interest, string? strategy = null) =>
        new(Tenant, project, name, false, null, null, power, interest, strategy, null);

    [Theory]
    [InlineData(PowerLevel.High, InterestLevel.High, StakeholderQuadrant.ManageClosely, StakeholderStrategyDefaults.ManageClosely)]
    [InlineData(PowerLevel.High, InterestLevel.Low, StakeholderQuadrant.KeepSatisfied, StakeholderStrategyDefaults.KeepSatisfied)]
    [InlineData(PowerLevel.Low, InterestLevel.High, StakeholderQuadrant.KeepInformed, StakeholderStrategyDefaults.KeepInformed)]
    [InlineData(PowerLevel.Low, InterestLevel.Low, StakeholderQuadrant.Monitor, StakeholderStrategyDefaults.Monitor)]
    [InlineData(PowerLevel.Medium, InterestLevel.Medium, StakeholderQuadrant.Monitor, StakeholderStrategyDefaults.Monitor)]
    public async Task AStakeholderWithNoWrittenStrategy_GetsTheQuadrantsDefault(PowerLevel power, InterestLevel interest, StakeholderQuadrant quadrant, string text)
    {
        var service = Build();

        var created = (await service.CreateAsync(New(Guid.NewGuid(), "Council", power, interest), default)).Value!;

        Assert.Equal(quadrant, created.Quadrant);
        Assert.Equal(text, created.Strategy);
        Assert.Null(created.EngagementStrategy); // nothing is stored for what is derived
    }

    [Fact]
    public async Task AWrittenStrategy_IsKept_AndWinsOverTheQuadrant()
    {
        var service = Build();

        var created = (await service.CreateAsync(New(Guid.NewGuid(), "Union", PowerLevel.High, InterestLevel.High, "Meet monthly"), default)).Value!;

        Assert.Equal("Meet monthly", created.Strategy);
    }

    [Fact]
    public async Task AProjectCanWordItsOwnStrategies_AndTheStakeholdersFollow_ForThatProjectOnly()
    {
        var service = Build();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        await service.CreateAsync(New(mine, "Board", PowerLevel.High, InterestLevel.High), default);
        await service.CreateAsync(New(other, "Board", PowerLevel.High, InterestLevel.High), default);

        var saved = await service.SaveSettingsAsync(new SaveStakeholderSettingsRequest(Tenant, mine, "Engage weekly", null, "  ", null), default);

        Assert.False(saved.Value!.IsDefault);
        Assert.Equal("Engage weekly", saved.Value.ManageClosely);
        Assert.Equal(StakeholderStrategyDefaults.KeepInformed, saved.Value.KeepInformed); // blank = default
        Assert.Equal("Engage weekly", (await service.ListByProjectAsync(mine, default)).Value!.Single().Strategy);
        Assert.Equal(StakeholderStrategyDefaults.ManageClosely, (await service.ListByProjectAsync(other, default)).Value!.Single().Strategy);

        var reset = await service.SaveSettingsAsync(new SaveStakeholderSettingsRequest(Tenant, mine, null, null, null, null), default);
        Assert.True(reset.Value!.IsDefault);
        Assert.Equal(StakeholderStrategyDefaults.ManageClosely, (await service.ListByProjectAsync(mine, default)).Value!.Single().Strategy);
    }

    [Fact]
    public async Task SettingsAndStakeholdersOfSeveralProjects_AreReadInOneCall()
    {
        var service = Build();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        await service.CreateAsync(New(a, "A1", PowerLevel.Low, InterestLevel.Low), default);
        await service.CreateAsync(New(b, "B1", PowerLevel.Low, InterestLevel.Low), default);
        await service.CreateAsync(New(c, "C1", PowerLevel.Low, InterestLevel.Low), default);
        await service.SaveSettingsAsync(new SaveStakeholderSettingsRequest(Tenant, a, null, null, null, "Just watch"), default);

        var people = (await service.ListForProjectsAsync(new ListStakeholdersForProjectsRequest([a, b, b]), default)).Value!;
        var settings = (await service.GetSettingsForProjectsAsync(new StakeholderSettingsForProjectsRequest([a, b]), default)).Value!;

        Assert.Equal(["A1", "B1"], people.Select(p => p.Name).Order());
        Assert.Equal("Just watch", people.Single(p => p.Name == "A1").Strategy);
        Assert.False(settings.Single(s => s.ProjectId == a).IsDefault);
        Assert.True(settings.Single(s => s.ProjectId == b).IsDefault);
    }

    [Fact]
    public async Task WithoutTheSettingsRepository_StakeholdersStillWork_WithTheDefaultWording()
    {
        var service = Build(withSettings: false);

        var created = (await service.CreateAsync(New(Guid.NewGuid(), "Press", PowerLevel.Low, InterestLevel.High), default)).Value!;

        Assert.Equal(StakeholderStrategyDefaults.KeepInformed, created.Strategy);
        Assert.Equal("conflict", (await service.SaveSettingsAsync(new SaveStakeholderSettingsRequest(Tenant, created.ProjectId, "x", null, null, null), default)).Error.Code);
    }

    private static StakeholderManagementDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<StakeholderManagementDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "stakeholder_management", "2026-10-12-add-stakeholder-settings.sql", typeof(StakeholderSchemaUpgrade), ["StakeholderProjectSettings"]);
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-12-add-stakeholder-settings.sql", typeof(StakeholderSchemaUpgrade));
    }
}
