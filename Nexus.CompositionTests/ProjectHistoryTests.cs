using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Core.Infrastructure;
using Nexus.ProjectManagement.History.Application;
using Nexus.ProjectManagement.History.Domain;
using Nexus.ProjectManagement.History.Infrastructure;
using NexusCore.Application.Common;
using NexusCore.Infrastructure.Persistence;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.CompositionTests;

public sealed class ProjectHistoryTests : IDisposable
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId { get; set; } = Alice;
        public Guid? TenantId { get; set; } = Tenant;
        public string? Email => null;
        public string? IpAddress => null;
    }

    private sealed class ThrowingObserver : IEntityChangeObserver
    {
        public int Calls { get; private set; }
        public Task OnChangesSavedAsync(IReadOnlyList<EntityChange> changes, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("observer exploded");
        }
    }

    private sealed class CapturingObserver : IEntityChangeObserver
    {
        public List<EntityChange> Seen { get; } = [];
        public Task OnChangesSavedAsync(IReadOnlyList<EntityChange> changes, CancellationToken cancellationToken)
        {
            Seen.AddRange(changes);
            return Task.CompletedTask;
        }
    }

    private readonly ServiceProvider _root;
    private readonly FakeUser _user = new();
    private readonly string _database = Guid.NewGuid().ToString();

    public ProjectHistoryTests() : this(withRecorder: true)
    {
    }

    private ProjectHistoryTests(bool withRecorder, params IEntityChangeObserver[] extraObservers)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserContext>(_user);
        services.AddScoped<AuditingInterceptor>();

        if (withRecorder)
        {
            services.AddScoped<IEntityChangeObserver, ProjectChangeRecorder>();
        }

        foreach (var observer in extraObservers)
        {
            services.AddSingleton(observer);
        }

        // The history's own context has no interceptors, exactly as AddProjectHistoryInfrastructure registers it.
        services.AddDbContext<ProjectHistoryDbContext>(o => o.UseInMemoryDatabase("history-" + _database));
        services.AddScoped<IProjectHistoryUnitOfWork>(p => p.GetRequiredService<ProjectHistoryDbContext>());
        services.AddScoped<IProjectHistoryRepository, ProjectHistoryRepository>();
        services.AddScoped<IProjectHistoryService, ProjectHistoryService>();

        services.AddDbContext<ContractsDbContext>((p, o) => o.UseInMemoryDatabase("contracts-" + _database).AddInterceptors(p.GetRequiredService<AuditingInterceptor>()));
        services.AddDbContext<ProjectManagementCoreDbContext>((p, o) => o.UseInMemoryDatabase("core-" + _database).AddInterceptors(p.GetRequiredService<AuditingInterceptor>()));
        _root = services.BuildServiceProvider();
    }

    public void Dispose() => _root.Dispose();

    private static IServiceProvider NewScopeOf(ServiceProvider root) => root.CreateScope().ServiceProvider;

    private async Task<IReadOnlyList<ProjectChange>> HistoryAsync(Guid? projectId = null)
    {
        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectHistoryDbContext>();
        return await db.ProjectChanges.Where(c => c.ProjectId == (projectId ?? ProjectId)).OrderBy(c => c.ChangedAtUtc).ToListAsync();
    }

    private static Contract NewContract(string title = "Road works") => new(Guid.NewGuid(), Tenant, ProjectId, "1405/12", title, "Acme", 1_000m);

    private static IReadOnlyList<string?[]> Changes(ProjectChange change) =>
        System.Text.Json.JsonSerializer.Deserialize<List<string?[]>>(change.ChangesJson!)!;

    // ------------------------------------------------------------------- recording

    [Fact]
    public async Task CreatingSomething_LogsOneAddedRow_WithWhoWhenAndTheNewValues()
    {
        var contract = NewContract();
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            db.Contracts.Add(contract);
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(await HistoryAsync());
        Assert.Equal("Contract", row.EntityName);
        Assert.Equal(contract.Id, row.EntityId);
        Assert.Equal(ProjectChangeKind.Added, row.Kind);
        Assert.Equal(Alice, row.ChangedByUserId);
        Assert.Equal(Tenant, row.TenantId);
        Assert.True(row.ChangedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));

        var changes = Changes(row);
        Assert.Contains(changes, c => c[0] == "Title" && c[1] is null && c[2] == "Road works");
        Assert.Contains(changes, c => c[0] == "OriginalAmount" && c[2] == "1000");
        // Ids, the tenant and the audit stamps are not "changes".
        Assert.DoesNotContain(changes, c => c[0] is "Id" or "TenantId" or "CreatedAtUtc" or "CreatedByUserId");
    }

    [Fact]
    public async Task EditingSomething_LogsOnlyThePropertiesThatChanged_WithOldAndNew()
    {
        var contract = NewContract();
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            db.Contracts.Add(contract);
            await db.SaveChangesAsync();
        }

        _user.UserId = Bob;
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id);
            loaded.UpdateDetails("1405/12", "Road works - phase 2", "Acme", null, null, null, null, 1_000m);
            await db.SaveChangesAsync();
        }

        var edit = (await HistoryAsync()).Single(c => c.Kind == ProjectChangeKind.Modified);
        Assert.Equal(Bob, edit.ChangedByUserId);
        var change = Assert.Single(Changes(edit)); // the number, party and amount were re-sent unchanged
        Assert.Equal(["Title", "Road works", "Road works - phase 2"], change);
    }

    [Fact]
    public async Task ASaveThatChangesNothing_LogsNothing()
    {
        var contract = NewContract();
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            db.Contracts.Add(contract);
            await db.SaveChangesAsync();
        }

        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            var loaded = await db.Contracts.SingleAsync(c => c.Id == contract.Id);
            loaded.UpdateDetails("1405/12", "Road works", "Acme", null, null, null, null, 1_000m); // same values
            await db.SaveChangesAsync();
        }

        Assert.Single(await HistoryAsync()); // only the creation
    }

    [Fact]
    public async Task DeletingSomething_LogsADeletedRow_WithTheLastValues()
    {
        var contract = NewContract("Doomed");
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            db.Contracts.Add(contract);
            await db.SaveChangesAsync();
        }

        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            db.Contracts.Remove(await db.Contracts.SingleAsync(c => c.Id == contract.Id));
            await db.SaveChangesAsync();
        }

        var deleted = (await HistoryAsync()).Single(c => c.Kind == ProjectChangeKind.Deleted);
        Assert.Equal(contract.Id, deleted.EntityId);
        Assert.Contains(Changes(deleted), c => c[0] == "Title" && c[1] == "Doomed" && c[2] is null);
    }

    [Fact]
    public async Task TheProjectItself_IsLoggedUnderItsOwnId_AndSoAreItsEdits()
    {
        var project = new Project(ProjectId, Tenant, "Dam", "DAM-1", ProjectType.Waterfall, null, null);
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProjectManagementCoreDbContext>();
            db.Projects.Add(project);
            await db.SaveChangesAsync();
        }

        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProjectManagementCoreDbContext>();
            (await db.Projects.SingleAsync(p => p.Id == ProjectId)).ChangeStatus(ProjectStatus.Active);
            await db.SaveChangesAsync();
        }

        var rows = await HistoryAsync();
        Assert.Equal([ProjectChangeKind.Added, ProjectChangeKind.Modified], rows.Select(r => r.Kind));
        Assert.All(rows, r => Assert.Equal("Project", r.EntityName));
        Assert.Equal([["Status", "Draft", "Active"]], Changes(rows[1]));
    }

    [Fact]
    public async Task ThingsWithNoProjectId_AreNotLogged()
    {
        var contractId = Guid.NewGuid();
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            // An addendum is identified by its contract, not by the project.
            db.ContractAddenda.Add(new ContractAddendum(Guid.NewGuid(), Tenant, contractId, 1, "Addendum", null, null, 10m, 0));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await HistoryAsync());
        using var check = _root.CreateScope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<ProjectHistoryDbContext>().ProjectChanges.ToListAsync());
    }

    [Fact]
    public async Task SeveralEntitiesInOneSave_AreEachLogged_AndAnotherProjectsAreKeptApart()
    {
        var otherProject = Guid.NewGuid();
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            db.Contracts.Add(NewContract());
            db.Contracts.Add(new Contract(Guid.NewGuid(), Tenant, otherProject, "X", "Other", "Y", 5m));
            await db.SaveChangesAsync();
        }

        Assert.Single(await HistoryAsync());
        Assert.Single(await HistoryAsync(otherProject));
    }

    [Fact]
    public async Task AVeryLongValue_IsCutAt500Characters()
    {
        var contract = NewContract(new string('x', 600));
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            db.Contracts.Add(contract);
            await db.SaveChangesAsync();
        }

        var title = Changes(Assert.Single(await HistoryAsync())).Single(c => c[0] == "Title")[2]!;
        Assert.Equal(500, title.Length);
    }

    // ------------------------------------------------------------------ safety

    [Fact]
    public async Task AnObserverThatThrows_NeverBreaksTheBusinessSave_AndOthersStillRun()
    {
        var thrower = new ThrowingObserver();
        var capturing = new CapturingObserver();
        using var tests = new ProjectHistoryTests(withRecorder: false, thrower, capturing);
        var contract = NewContract();

        using (var scope = tests._root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            db.Contracts.Add(contract);
            var saved = await db.SaveChangesAsync();
            Assert.Equal(1, saved);
        }

        using (var scope = tests._root.CreateScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ContractsDbContext>().Contracts.CountAsync()); // really stored
        }

        Assert.Equal(1, thrower.Calls);
        Assert.Equal(contract.Id, Assert.Single(capturing.Seen).EntityId);
    }

    [Fact]
    public async Task WithNoObserverRegistered_NothingIsCapturedAndSavingWorksAsBefore()
    {
        using var tests = new ProjectHistoryTests(withRecorder: false);

        using (var scope = tests._root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            db.Contracts.Add(NewContract());
            Assert.Equal(1, await db.SaveChangesAsync());
            var contract = await db.Contracts.SingleAsync();
            Assert.Equal(Alice, contract.CreatedByUserId); // the existing audit stamping still happens
        }

        using var check = tests._root.CreateScope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<ProjectHistoryDbContext>().ProjectChanges.ToListAsync());
    }

    [Fact]
    public async Task AFailedSave_LogsNothing_AndDoesNotLeakIntoTheNextSaveOfTheSameContext()
    {
        var first = NewContract();
        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            db.Contracts.Add(first);
            await db.SaveChangesAsync();
        }

        var afterFirst = (await HistoryAsync()).Count;

        using var other = _root.CreateScope();
        var context = other.ServiceProvider.GetRequiredService<ContractsDbContext>();
        context.Contracts.Add(new Contract(first.Id, Tenant, ProjectId, "dup", "Duplicate id", "C", 1m)); // same key: the save fails
        await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        Assert.Equal(afterFirst, (await HistoryAsync()).Count);

        context.Contracts.Add(new Contract(Guid.NewGuid(), Tenant, ProjectId, "2", "Second", "C", 1m));
        await context.SaveChangesAsync();

        var rows = await HistoryAsync();
        Assert.Equal(afterFirst + 1, rows.Count); // exactly the second contract, not the failed one as well
        Assert.DoesNotContain(rows, r => Changes(r).Any(c => c[2] == "Duplicate id"));
    }

    [Fact]
    public void TheHistoryOwnRowsAreNeverRecorded()
    {
        Assert.Contains(nameof(ProjectChange), ProjectChangeRecorder.ExcludedEntities);
        Assert.Contains("SprintEvent", ProjectChangeRecorder.ExcludedEntities);
    }

    // ------------------------------------------------------------------- query

    private async Task SeedAsync(int count, Guid? tenant = null, string entity = "Risk", ProjectChangeKind kind = ProjectChangeKind.Modified, Guid? user = null)
    {
        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectHistoryDbContext>();
        for (var i = 0; i < count; i++)
        {
            db.ProjectChanges.Add(new ProjectChange(
                Guid.NewGuid(), tenant ?? Tenant, ProjectId, entity, Guid.NewGuid(), kind, user ?? Alice,
                DateTimeOffset.UtcNow.AddMinutes(-count + i), """[["Title","a","b"]]"""));
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task TheQuery_ReturnsNewestFirst_WithPaging_AndParsedChanges()
    {
        await SeedAsync(5);
        using var scope = _root.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IProjectHistoryService>();

        var page = (await service.QueryAsync(new ProjectHistoryQuery(Tenant, ProjectId, Skip: 1, Take: 2), default)).Value!;

        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.Items[0].ChangedAtUtc > page.Items[1].ChangedAtUtc);
        Assert.Equal(new ProjectChangePropertyDtoShim("Title", "a", "b"), page.Items[0].Changes.Select(c => new ProjectChangePropertyDtoShim(c.Property, c.OldValue, c.NewValue)).Single());
    }

    private sealed record ProjectChangePropertyDtoShim(string Property, string? OldValue, string? NewValue);

    [Fact]
    public async Task TheQuery_Filters_AndOnlyEverShowsTheCallersTenant()
    {
        await SeedAsync(2, entity: "Risk", kind: ProjectChangeKind.Added, user: Alice);
        await SeedAsync(3, entity: "Stakeholder", kind: ProjectChangeKind.Modified, user: Bob);
        await SeedAsync(4, tenant: Guid.NewGuid(), entity: "Risk"); // someone else's tenant, same project id
        using var scope = _root.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IProjectHistoryService>();

        Assert.Equal(5, (await service.QueryAsync(new ProjectHistoryQuery(Tenant, ProjectId), default)).Value!.Total);
        Assert.Equal(2, (await service.QueryAsync(new ProjectHistoryQuery(Tenant, ProjectId, EntityName: "Risk"), default)).Value!.Total);
        Assert.Equal(3, (await service.QueryAsync(new ProjectHistoryQuery(Tenant, ProjectId, Kind: ProjectChangeKind.Modified), default)).Value!.Total);
        Assert.Equal(3, (await service.QueryAsync(new ProjectHistoryQuery(Tenant, ProjectId, UserId: Bob), default)).Value!.Total);
        Assert.Equal(0, (await service.QueryAsync(new ProjectHistoryQuery(Tenant, ProjectId, From: DateTimeOffset.UtcNow.AddDays(1)), default)).Value!.Total);
        Assert.Equal(5, (await service.QueryAsync(new ProjectHistoryQuery(Tenant, ProjectId, To: DateTimeOffset.UtcNow.AddDays(1)), default)).Value!.Total);
    }

    [Fact]
    public async Task TheQuery_ClampsThePageSize_AndValidates()
    {
        using var scope = _root.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IProjectHistoryService>();

        var page = (await service.QueryAsync(new ProjectHistoryQuery(Tenant, ProjectId, Skip: -5, Take: 100_000), default)).Value!;
        Assert.Equal(0, page.Skip);
        Assert.Equal(ProjectHistoryService.MaxPageSize, page.Take);
        Assert.Equal(1, (await service.QueryAsync(new ProjectHistoryQuery(Tenant, ProjectId, Take: 0), default)).Value!.Take);

        Assert.Equal("validation.error", (await service.QueryAsync(new ProjectHistoryQuery(Tenant, Guid.Empty), default)).Error.Code);
        var backwards = new ProjectHistoryQuery(Tenant, ProjectId, From: DateTimeOffset.UtcNow, To: DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Equal("validation.error", (await service.QueryAsync(backwards, default)).Error.Code);
    }

    // ------------------------------------------------------------------ schema

    [Fact]
    public void TheCreationScriptAndHelper_MatchTheModel()
    {
        using var db = new ProjectHistoryDbContext(
            new DbContextOptionsBuilder<ProjectHistoryDbContext>()
                .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True").Options);

        SchemaUpgradeVerifier.AssertMatchesModel(
            db, "project_history", "2026-10-04-add-project-history.sql", typeof(ProjectHistorySchemaUpgrade), ["ProjectChanges"]);
        SchemaUpgradeVerifier.AssertAdditiveOnly("2026-10-04-add-project-history.sql", typeof(ProjectHistorySchemaUpgrade));
    }
}
