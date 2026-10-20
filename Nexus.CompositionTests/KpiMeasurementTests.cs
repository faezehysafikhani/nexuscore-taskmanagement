using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Kpi.Application;
using Nexus.ProjectManagement.Kpi.Application.Dtos;
using Nexus.ProjectManagement.Kpi.Application.Validators;
using Nexus.ProjectManagement.Kpi.Domain;
using Nexus.ProjectManagement.Kpi.Infrastructure;

namespace Nexus.CompositionTests;

public sealed class KpiMeasurementTests
{
    private const string ScriptFile = "2026-10-19-add-kpi-measurements.sql";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed class FakeUnitOfWork : IKpiUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeKpis(params KpiDefinition[] seed) : IKpiRepository
    {
        public List<KpiDefinition> Items { get; } = [.. seed];
        public Task<KpiDefinition?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(k => k.Id == id));
        public Task<IReadOnlyList<KpiDefinition>> ListByProjectAsync(Guid projectId, Guid? deliverableId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KpiDefinition>>(Items.Where(k => k.ProjectId == projectId).ToList());
        public Task AddAsync(KpiDefinition kpi, CancellationToken ct) { Items.Add(kpi); return Task.CompletedTask; }
        public Task RemoveAsync(KpiDefinition kpi, CancellationToken ct) { Items.Remove(kpi); return Task.CompletedTask; }
    }

    private sealed class FakeMeasurements : IKpiMeasurementRepository
    {
        public List<KpiMeasurement> Items { get; } = [];
        public Task<KpiMeasurement?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(m => m.Id == id));
        public Task<IReadOnlyList<KpiMeasurement>> ListByKpiAsync(Guid kpiDefinitionId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KpiMeasurement>>(Items.Where(m => m.KpiDefinitionId == kpiDefinitionId).OrderBy(m => m.MeasuredOn).ToList());
        public Task<IReadOnlyList<KpiMeasurement>> ListByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KpiMeasurement>>(Items.Where(m => projectIds.Contains(m.ProjectId)).ToList());
        public Task AddAsync(KpiMeasurement measurement, CancellationToken ct) { Items.Add(measurement); return Task.CompletedTask; }
        public Task RemoveAsync(KpiMeasurement measurement, CancellationToken ct) { Items.Remove(measurement); return Task.CompletedTask; }
        public Task RemoveByKpiAsync(Guid kpiDefinitionId, CancellationToken ct) { Items.RemoveAll(m => m.KpiDefinitionId == kpiDefinitionId); return Task.CompletedTask; }
    }

    private static KpiDefinition NewKpi() => new(Guid.NewGuid(), Tenant, Project, Guid.NewGuid(), KpiType.Lag, "defect rate");

    private static (KpiMeasurementService Service, FakeMeasurements Measurements, FakeUnitOfWork Unit) Create(params KpiDefinition[] kpis)
    {
        var measurements = new FakeMeasurements();
        var unit = new FakeUnitOfWork();
        return (new KpiMeasurementService(new FakeKpis(kpis), measurements, unit), measurements, unit);
    }

    [Fact]
    public async Task Record_TakesTenantAndProjectFromTheKpi_AndKeepsTheValue()
    {
        var kpi = NewKpi();
        var (service, measurements, unit) = Create(kpi);

        var result = await service.RecordAsync(kpi.Id, new RecordKpiMeasurementRequest(Today, 42.5m, "  after the audit "), default);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(measurements.Items);
        Assert.Equal(Tenant, stored.TenantId);
        Assert.Equal(Project, stored.ProjectId);
        Assert.Equal(kpi.Id, stored.KpiDefinitionId);
        Assert.Equal(42.5m, result.Value!.Value);
        Assert.Equal("after the audit", result.Value.Note);
        Assert.Equal(1, unit.Saves);
    }

    [Fact]
    public async Task Record_ForAnUnknownKpi_IsNotFound_AndSavesNothing()
    {
        var (service, measurements, unit) = Create();

        var result = await service.RecordAsync(Guid.NewGuid(), new RecordKpiMeasurementRequest(Today, 1m, null), default);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(measurements.Items);
        Assert.Equal(0, unit.Saves);
    }

    [Fact]
    public async Task List_ReturnsTheKpisValuesOldestFirst_AndOnlyThoseOfThatKpi()
    {
        var kpi = NewKpi();
        var other = NewKpi();
        var (service, _, _) = Create(kpi, other);
        await service.RecordAsync(kpi.Id, new RecordKpiMeasurementRequest(Today, 3m, null), default);
        await service.RecordAsync(kpi.Id, new RecordKpiMeasurementRequest(Today.AddDays(-10), 1m, null), default);
        await service.RecordAsync(other.Id, new RecordKpiMeasurementRequest(Today, 99m, null), default);

        var result = await service.ListAsync(kpi.Id, default);

        Assert.Equal([1m, 3m], result.Value!.Select(m => m.Value).ToArray());
    }

    [Fact]
    public async Task Update_AndDelete_RefuseAValueThatBelongsToAnotherKpi()
    {
        var kpi = NewKpi();
        var other = NewKpi();
        var (service, measurements, _) = Create(kpi, other);
        var recorded = (await service.RecordAsync(kpi.Id, new RecordKpiMeasurementRequest(Today, 5m, null), default)).Value!;

        var update = await service.UpdateAsync(other.Id, recorded.Id, new UpdateKpiMeasurementRequest(Today, 6m, null), default);
        var delete = await service.DeleteAsync(other.Id, recorded.Id, default);

        Assert.Equal("not_found", update.Error.Code);
        Assert.Equal("not_found", delete.Error.Code);
        Assert.Equal(5m, Assert.Single(measurements.Items).Value);
    }

    [Fact]
    public async Task Update_ChangesTheValueAndDate_AndDelete_RemovesIt()
    {
        var kpi = NewKpi();
        var (service, measurements, _) = Create(kpi);
        var recorded = (await service.RecordAsync(kpi.Id, new RecordKpiMeasurementRequest(Today, 5m, null), default)).Value!;

        var updated = await service.UpdateAsync(kpi.Id, recorded.Id, new UpdateKpiMeasurementRequest(Today.AddDays(-1), 7m, "fixed"), default);
        Assert.Equal(7m, updated.Value!.Value);
        Assert.Equal(Today.AddDays(-1), updated.Value.MeasuredOn);

        Assert.True((await service.DeleteAsync(kpi.Id, recorded.Id, default)).IsSuccess);
        Assert.Empty(measurements.Items);
    }

    [Fact]
    public async Task DeletingAKpi_TakesItsValuesWithIt_ButNotAnotherKpisValues()
    {
        var kpi = NewKpi();
        var other = NewKpi();
        var kpis = new FakeKpis(kpi, other);
        var measurements = new FakeMeasurements();
        var unit = new FakeUnitOfWork();
        var measuring = new KpiMeasurementService(kpis, measurements, unit);
        await measuring.RecordAsync(kpi.Id, new RecordKpiMeasurementRequest(Today, 1m, null), default);
        await measuring.RecordAsync(other.Id, new RecordKpiMeasurementRequest(Today, 2m, null), default);

        var service = new KpiService(kpis, deliverableRepository: null!, unit, measurements);
        Assert.True((await service.DeleteAsync(kpi.Id, default)).IsSuccess);

        Assert.Equal(other.Id, Assert.Single(measurements.Items).KpiDefinitionId);
    }

    [Fact]
    public async Task DeletingAKpi_StillWorksWhereNoMeasurementRepositoryIsRegistered()
    {
        var kpi = NewKpi();
        var service = new KpiService(new FakeKpis(kpi), deliverableRepository: null!, new FakeUnitOfWork());

        Assert.True((await service.DeleteAsync(kpi.Id, default)).IsSuccess);
    }

    [Fact]
    public void TheValidators_RefuseAMissingOrFutureDate_AndAnOverlongNote()
    {
        var record = new RecordKpiMeasurementRequestValidator();
        var update = new UpdateKpiMeasurementRequestValidator();

        Assert.True(record.Validate(new RecordKpiMeasurementRequest(Today, -3m, null)).IsValid); // a measured value may be negative
        Assert.False(record.Validate(new RecordKpiMeasurementRequest(default, 1m, null)).IsValid);
        Assert.False(record.Validate(new RecordKpiMeasurementRequest(Today.AddDays(30), 1m, null)).IsValid);
        Assert.False(record.Validate(new RecordKpiMeasurementRequest(Today, 1m, new string('x', 1001))).IsValid);
        Assert.False(update.Validate(new UpdateKpiMeasurementRequest(Today.AddDays(30), 1m, null)).IsValid);
        Assert.True(update.Validate(new UpdateKpiMeasurementRequest(Today, 1m, new string('x', 1000))).IsValid);
    }

    // ------------------------------------------------------------------ schema upgrade

    private static KpiDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<KpiDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheModel()
    {
        using var db = NewSqlServerContext();

        SchemaUpgradeVerifier.AssertMatchesModel(db, "kpi", ScriptFile, typeof(KpiSchemaUpgrade), ["KpiMeasurements"]);
    }

    [Fact]
    public void TheUpgradeScript_OnlyCreates()
    {
        SchemaUpgradeVerifier.AssertAdditiveOnly(ScriptFile, typeof(KpiSchemaUpgrade));
    }

    [Fact]
    public void TheModel_IndexesAKpisValuesByDate_AndKeepsDefinitionsUntouched()
    {
        using var db = NewSqlServerContext();
        var generated = db.Database.GenerateCreateScript();

        Assert.Contains("CREATE INDEX [IX_KpiMeasurements_KpiDefinitionId_MeasuredOn] ON [kpi].[KpiMeasurements] ([KpiDefinitionId], [MeasuredOn])", generated);
        Assert.Contains("[Value] decimal(18,4) NOT NULL", generated);
        // The definitions table is not part of this upgrade: it must not need a new column.
        Assert.Contains("CREATE TABLE [kpi].[KpiDefinitions]", generated);
        Assert.DoesNotContain("ALTER TABLE [kpi].[KpiDefinitions]", ReadScriptText());
    }

    private static string ReadScriptText() => SchemaUpgradeVerifier.ReadScript(ScriptFile);
}
