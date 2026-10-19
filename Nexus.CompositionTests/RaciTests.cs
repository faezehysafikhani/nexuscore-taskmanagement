using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Raci.Application;
using Nexus.ProjectManagement.Raci.Application.Dtos;
using Nexus.ProjectManagement.Raci.Domain;
using Nexus.ProjectManagement.Raci.Infrastructure;

namespace Nexus.CompositionTests;

public sealed class RaciTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Activity = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();

    private static (RaciService Service, RaciDbContext Db) Create()
    {
        var db = new RaciDbContext(new DbContextOptionsBuilder<RaciDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new RaciService(new RaciRepository(db), db), db);
    }

    private static SetItemRaciRequest Set(Guid item, params RaciEntryInput[] entries) => new(Project, RaciItemType.WaterfallActivity, item, entries);

    [Fact]
    public async Task SetItem_StoresTheRoles_AndAPersonMayHoldSeveralOnOneItem()
    {
        var (service, _) = Create();
        var result = await service.SetItemAsync(Tenant, Set(Activity, new RaciEntryInput(Alice, RaciRole.Responsible), new(Alice, RaciRole.Accountable), new(Bob, RaciRole.Consulted), new(Carol, RaciRole.Informed)), default);

        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.Equal(4, result.Value!.Count);
        Assert.Equal(2, result.Value.Count(a => a.UserId == Alice));
    }

    [Fact]
    public async Task SetItem_ReplacesTheItemsRoles_AndLeavesOtherItemsAlone()
    {
        var (service, db) = Create();
        var other = Guid.NewGuid();
        await service.SetItemAsync(Tenant, Set(Activity, new RaciEntryInput(Alice, RaciRole.Responsible), new(Bob, RaciRole.Informed)), default);
        await service.SetItemAsync(Tenant, Set(other, new RaciEntryInput(Carol, RaciRole.Responsible)), default);

        var after = (await service.SetItemAsync(Tenant, Set(Activity, new RaciEntryInput(Alice, RaciRole.Responsible), new(Carol, RaciRole.Accountable)), default)).Value!;

        Assert.Equal(3, after.Count);
        Assert.Contains(after, a => a.ItemId == Activity && a.UserId == Carol && a.Role == RaciRole.Accountable);
        Assert.DoesNotContain(after, a => a.ItemId == Activity && a.UserId == Bob);
        Assert.Single(after, a => a.ItemId == other);

        var cleared = (await service.SetItemAsync(Tenant, Set(Activity), default)).Value!;
        Assert.Single(cleared);
        Assert.Equal(1, await db.RaciAssignments.CountAsync());
    }

    [Fact]
    public async Task SetItem_SavingTheSameRolesTwice_ChangesNothing()
    {
        var (service, db) = Create();
        var request = Set(Activity, new RaciEntryInput(Alice, RaciRole.Responsible), new(Alice, RaciRole.Responsible), new(Bob, RaciRole.Accountable));
        await service.SetItemAsync(Tenant, request, default);
        var ids = await db.RaciAssignments.Select(a => a.Id).ToListAsync();

        await service.SetItemAsync(Tenant, request, default);

        Assert.Equal(2, ids.Count); // the duplicate entry was one role
        Assert.Equal(ids.Order(), (await db.RaciAssignments.Select(a => a.Id).ToListAsync()).Order());
    }

    [Fact]
    public async Task SetItem_AllowsOnlyOneAccountablePerson_AndRejectsBadInput()
    {
        var (service, _) = Create();
        Assert.Equal("validation.error", (await service.SetItemAsync(Tenant, Set(Activity, new RaciEntryInput(Alice, RaciRole.Accountable), new(Bob, RaciRole.Accountable)), default)).Error.Code);
        Assert.Equal("validation.error", (await service.SetItemAsync(Tenant, Set(Guid.Empty, new RaciEntryInput(Alice, RaciRole.Responsible)), default)).Error.Code);
        Assert.Equal("validation.error", (await service.SetItemAsync(Tenant, Set(Activity, new RaciEntryInput(Guid.Empty, RaciRole.Responsible)), default)).Error.Code);
        Assert.Equal("validation.error", (await service.SetItemAsync(Tenant, Set(Activity, new RaciEntryInput(Alice, (RaciRole)9)), default)).Error.Code);
    }

    [Fact]
    public async Task AnotherTenant_SeesNothing_AndCannotChangeTheMatrix()
    {
        var (service, db) = Create();
        await service.SetItemAsync(Tenant, Set(Activity, new RaciEntryInput(Alice, RaciRole.Responsible)), default);
        var stranger = Guid.NewGuid();

        Assert.Empty((await service.ListByProjectAsync(stranger, Project, default)).Value!);
        await service.SetItemAsync(stranger, Set(Activity), default); // clearing "their" (empty) view of the item
        Assert.Equal(1, await db.RaciAssignments.CountAsync(a => a.TenantId == Tenant));
    }
}
