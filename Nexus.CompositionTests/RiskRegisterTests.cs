using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Application.Dtos;
using Nexus.ProjectManagement.RiskManagement.Application.Validators;
using Nexus.ProjectManagement.RiskManagement.Domain;
using Nexus.ProjectManagement.RiskManagement.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

public sealed class RiskRegisterTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class NoApproval : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) =>
            Task.FromResult(ApprovalRequestOutcome.NotConfigured);
    }

    private static (RiskService Service, RiskManagementDbContext Db) Build(bool withSettings = true)
    {
        var db = new RiskManagementDbContext(new DbContextOptionsBuilder<RiskManagementDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new RiskService(new RiskRepository(db), db, new NoApproval(), withSettings ? new RiskSettingsRepository(db) : null), db);
    }

    private static CreateRiskRequest ByLevel(Guid project, string probability, string impact, string description = "Supplier delay") =>
        new(Tenant, project, description, 0, 0, 0, "Dual sourcing", null, probability, impact);

    // ------------------------------------------------------------ levels

    [Theory]
    [InlineData("low", 1)]
    [InlineData("medium", 3)]
    [InlineData("high", 5)]
    [InlineData("HIGH", 5)]
    public async Task ABandStandsForAScore_AndReadsBackAsTheSameBand(string band, int score)
    {
        var (service, _) = Build();

        var risk = (await service.CreateAsync(ByLevel(Guid.NewGuid(), band, band), default)).Value!;

        Assert.Equal(score, risk.ProbabilityScore);
        Assert.Equal(score, risk.ImpactScore);
        Assert.Equal(score, risk.SeverityScore); // severity follows the impact when only a band is given
        Assert.Equal(band.ToLowerInvariant(), risk.ProbabilityLevel);
        Assert.Equal(band.ToLowerInvariant(), risk.ImpactLevel);
    }

    [Theory]
    [InlineData(1, "low")]
    [InlineData(2, "low")]
    [InlineData(3, "medium")]
    [InlineData(4, "high")]
    [InlineData(5, "high")]
    public async Task AScoreIsReadAsABand(int score, string band)
    {
        var (service, _) = Build();

        var risk = (await service.CreateAsync(new CreateRiskRequest(Tenant, Guid.NewGuid(), "Scored risk", score, 2, score, null, null), default)).Value!;

        Assert.Equal(band, risk.ProbabilityLevel);
        Assert.Equal(band, risk.ImpactLevel);
    }

    [Fact]
    public async Task TheRequestsThatNeverHeardOfBands_WorkExactlyAsBefore()
    {
        var (service, _) = Build(withSettings: false);

        var created = await service.CreateAsync(new CreateRiskRequest(Tenant, Guid.NewGuid(), "Old style", 4, 2, 5, "plan", null), default);

        Assert.Equal((4, 2, 5), (created.Value!.ProbabilityScore, created.Value.SeverityScore, created.Value.ImpactScore));
        Assert.Equal(40, created.Value.Rpn);
    }

    [Fact]
    public void ScoresAreCheckedOnlyWhenNoBandIsGiven_AndABandMustBeOne()
    {
        var validator = new CreateRiskRequestValidator();
        var project = Guid.NewGuid();

        Assert.True(validator.Validate(ByLevel(project, "low", "high")).IsValid);
        Assert.False(validator.Validate(new CreateRiskRequest(Tenant, project, "x", 0, 0, 0, null, null)).IsValid);
        Assert.False(validator.Validate(ByLevel(project, "extreme", "high")).IsValid);
        Assert.False(validator.Validate(ByLevel(project, "low", "none")).IsValid);
    }

    // ------------------------------------------------------------ settings

    [Fact]
    public async Task AProjectWithoutSettings_GetsTheDefaults_AndTheDefaultColourOfItsCell()
    {
        var (service, _) = Build();
        var project = Guid.NewGuid();
        await service.CreateAsync(ByLevel(project, "high", "high"), default);
        await service.CreateAsync(ByLevel(project, "low", "low", "Minor"), default);

        var settings = (await service.GetSettingsAsync(project, default)).Value!;
        var risks = (await service.ListByProjectAsync(project, default)).Value!;

        Assert.True(settings.IsDefault);
        Assert.Equal(6, settings.Definitions.Count);
        Assert.Equal(9, settings.MatrixColors.Count);
        Assert.Equal("red", risks.Single(r => r.ProbabilityLevel == "high").Level);
        Assert.Equal("green", risks.Single(r => r.ProbabilityLevel == "low").Level);
    }

    [Fact]
    public async Task ChangingACellColour_ChangesTheLevelOfTheRisksInIt_ForThatProjectOnly()
    {
        var (service, _) = Build();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        await service.CreateAsync(ByLevel(mine, "medium", "medium"), default);
        await service.CreateAsync(ByLevel(other, "medium", "medium"), default);

        var colours = (await service.GetSettingsAsync(mine, default)).Value!.MatrixColors
            .Select(c => c.Probability == "medium" && c.Impact == "medium" ? c with { Color = "red" } : c).ToList();
        var saved = await service.SaveSettingsAsync(new SaveRiskSettingsRequest(Tenant, mine, null, colours), default);

        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.Message : null);
        Assert.False(saved.Value!.IsDefault);
        Assert.Equal("red", (await service.ListByProjectAsync(mine, default)).Value!.Single().Level);
        Assert.Equal("yellow", (await service.ListByProjectAsync(other, default)).Value!.Single().Level);
    }

    [Fact]
    public async Task Definitions_CanBeSet_LeftAloneAndResetToTheDefault()
    {
        var (service, _) = Build();
        var project = Guid.NewGuid();
        var definitions = (await service.GetSettingsAsync(project, default)).Value!.Definitions
            .Select(d => d with { Description = "custom " + d.Axis + " " + d.Level }).ToList();

        await service.SaveSettingsAsync(new SaveRiskSettingsRequest(Tenant, project, definitions, null), default);
        Assert.Equal("custom impact high", (await service.GetSettingsAsync(project, default)).Value!.Definitions.Single(d => d.Axis == "impact" && d.Level == "high").Description);

        // a save that names only the colours leaves the definitions alone
        await service.SaveSettingsAsync(new SaveRiskSettingsRequest(Tenant, project, null, []), default);
        Assert.StartsWith("custom", (await service.GetSettingsAsync(project, default)).Value!.Definitions[0].Description);

        // an empty list puts that part back on the default
        var reset = await service.SaveSettingsAsync(new SaveRiskSettingsRequest(Tenant, project, [], null), default);
        Assert.True(reset.Value!.IsDefault);
        Assert.DoesNotContain("custom", reset.Value.Definitions[0].Description);
    }

    [Fact]
    public async Task IncompleteOrUnknownSettings_AreRefused_AndNothingIsStored()
    {
        var (service, _) = Build();
        var project = Guid.NewGuid();
        var defaults = (await service.GetSettingsAsync(project, default)).Value!;

        var fewColours = await service.SaveSettingsAsync(new SaveRiskSettingsRequest(Tenant, project, null, defaults.MatrixColors.Take(8).ToList()), default);
        var badColour = await service.SaveSettingsAsync(new SaveRiskSettingsRequest(Tenant, project, null,
            defaults.MatrixColors.Select((c, i) => i == 0 ? c with { Color = "purple" } : c).ToList()), default);
        var duplicate = await service.SaveSettingsAsync(new SaveRiskSettingsRequest(Tenant, project, defaults.Definitions.Select((d, i) => i == 0 ? d with { Level = "medium" } : d).ToList(), null), default);

        Assert.Equal("validation.error", fewColours.Error.Code);
        Assert.Equal("validation.error", badColour.Error.Code);
        Assert.Equal("validation.error", duplicate.Error.Code);
        Assert.True((await service.GetSettingsAsync(project, default)).Value!.IsDefault);
    }

    [Fact]
    public async Task SettingsForSeveralProjects_AreReadInOneCall_WithDefaultsFilledIn()
    {
        var (service, _) = Build();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var colours = (await service.GetSettingsAsync(a, default)).Value!.MatrixColors.Select(c => c with { Color = "green" }).ToList();
        await service.SaveSettingsAsync(new SaveRiskSettingsRequest(Tenant, a, null, colours), default);

        var all = (await service.GetSettingsForProjectsAsync(new RiskSettingsForProjectsRequest([a, b, b]), default)).Value!;

        Assert.Equal(2, all.Count);
        Assert.All(all.Single(s => s.ProjectId == a).MatrixColors, c => Assert.Equal("green", c.Color));
        Assert.True(all.Single(s => s.ProjectId == b).IsDefault);
    }

    [Fact]
    public async Task WithoutTheSettingsRepository_RisksStillWork_WithDefaultColours()
    {
        var (service, _) = Build(withSettings: false);
        var project = Guid.NewGuid();
        await service.CreateAsync(ByLevel(project, "high", "medium"), default);

        Assert.Equal("red", (await service.ListByProjectAsync(project, default)).Value!.Single().Level);
        Assert.Equal("conflict", (await service.SaveSettingsAsync(new SaveRiskSettingsRequest(Tenant, project, null, []), default)).Error.Code);
    }

    // ------------------------------------------------------------ response status

    [Fact]
    public async Task OnlyAnApprovedRisk_CanHaveItsResponseStatusChanged()
    {
        var (service, _) = Build();
        var risk = (await service.CreateAsync(ByLevel(Guid.NewGuid(), "high", "high"), default)).Value!;
        Assert.Equal(RiskResponseStatus.Planning, risk.ResponseStatus);

        var early = await service.ChangeResponseStatusAsync(risk.Id, new ChangeRiskResponseStatusRequest(RiskResponseStatus.InProgress), default);
        Assert.Equal("conflict", early.Error.Code);

        await service.SubmitForApprovalAsync(risk.Id, default); // no workflow: approved at once
        var moved = await service.ChangeResponseStatusAsync(risk.Id, new ChangeRiskResponseStatusRequest(RiskResponseStatus.Completed), default);

        Assert.Equal(RiskResponseStatus.Completed, moved.Value!.ResponseStatus);
        Assert.Equal("validation.error", (await service.ChangeResponseStatusAsync(risk.Id, new ChangeRiskResponseStatusRequest((RiskResponseStatus)9), default)).Error.Code);
        Assert.Equal("not_found", (await service.ChangeResponseStatusAsync(Guid.NewGuid(), new ChangeRiskResponseStatusRequest(RiskResponseStatus.OnHold), default)).Error.Code);
    }

    // ------------------------------------------------------------ several projects

    [Fact]
    public async Task TheRisksOfSeveralProjects_AreReadInOneCall()
    {
        var (service, _) = Build();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var p3 = Guid.NewGuid();
        await service.CreateAsync(ByLevel(p1, "low", "low", "one"), default);
        await service.CreateAsync(ByLevel(p2, "low", "low", "two"), default);
        await service.CreateAsync(ByLevel(p3, "low", "low", "three"), default);

        var risks = (await service.ListForProjectsAsync(new ListRisksForProjectsRequest([p1, p2, p2]), default)).Value!;

        Assert.Equal(["one", "two"], risks.Select(r => r.Description).Order());
        Assert.Empty((await service.ListForProjectsAsync(new ListRisksForProjectsRequest([]), default)).Value!);
    }

    // ------------------------------------------------------------ schema

    private static RiskManagementDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<RiskManagementDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "risk_management", "2026-10-12-add-risk-response-and-settings.sql", typeof(RiskSchemaUpgrade),
            ["RiskProjectSettings"],
            ("Risks", "[ResponseStatus] int NOT NULL"));
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-12-add-risk-response-and-settings.sql", typeof(RiskSchemaUpgrade));
    }
}
