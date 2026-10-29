using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Domain;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure.Domain;
using Nexus.OrgStructure.Infrastructure;

namespace Nexus.CompositionTests;

/// <summary>The dynamic structure's data (1.1): stored and read back, and a plain unit / company / post stays what it was.</summary>
public sealed class OrgNodeModelTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static OrgStructureDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("node-" + name).Options);

    [Fact]
    public async Task NewTables_AreStoredAndReadBackByTheRepository()
    {
        var db = Db(Guid.NewGuid().ToString());
        var repository = new OrgStructureRepository(db);
        var bank = new NodeType(Guid.NewGuid(), Tenant, "deputy", "معاونت");
        var office = new NodeType(Guid.NewGuid(), Tenant, "office", "اداره کل", isDomainBoundary: false, sortOrder: 2);
        var dotted = new LinkType(Guid.NewGuid(), Tenant, "dotted", "نقطه‌چین", countsAsManager: false, "dashed");
        var template = new StructureTemplate(Guid.NewGuid(), Tenant, "bank", "بانکی", null, isSystem: true);
        db.AddRange(
            bank, office, dotted, template,
            new NodeTypeRule(Guid.NewGuid(), Tenant, null, bank.Id),
            new NodeTypeRule(Guid.NewGuid(), Tenant, bank.Id, office.Id),
            new NodeTypePostTemplate(Guid.NewGuid(), Tenant, bank.Id, Guid.NewGuid(), isManagerPost: true, 0),
            new AutoLinkRule(Guid.NewGuid(), Tenant, office.Id, bank.Id, dotted.Id),
            new OrgLink(Guid.NewGuid(), Tenant, Guid.NewGuid(), Guid.NewGuid(), dotted.Id),
            new StructureTemplateItem(Guid.NewGuid(), Tenant, template.Id, "d1", null, "deputy", "معاونت فناوری", 0),
            new OrgChangeLog(Guid.NewGuid(), Tenant, "unit", Guid.NewGuid(), "created", null, DateTimeOffset.UtcNow, "{}"));
        await db.SaveChangesAsync();

        Assert.Equal(["deputy", "office"], (await repository.ListNodeTypesAsync(Tenant, default)).Select(t => t.Code).ToArray());
        Assert.True((await repository.ListNodeTypesAsync(Tenant, default)).Single(t => t.Code == "deputy").IsDomainBoundary);
        Assert.False((await repository.ListNodeTypesAsync(Tenant, default)).Single(t => t.Code == "office").IsDomainBoundary);
        Assert.Equal(2, (await repository.ListNodeTypeRulesAsync(Tenant, default)).Count);
        Assert.Single(await repository.ListNodeTypePostTemplatesAsync(Tenant, default));
        Assert.False((await repository.ListLinkTypesAsync(Tenant, default)).Single().CountsAsManager);
        Assert.Single(await repository.ListAutoLinkRulesAsync(Tenant, default));
        Assert.Single(await repository.ListOrgLinksAsync(Tenant, default));
        Assert.Single(await repository.ListStructureTemplatesAsync(Tenant, default));
        Assert.Single(await repository.ListStructureTemplateItemsAsync(Tenant, template.Id, default));
        Assert.Single(await repository.ListOrgChangeLogsAsync(Tenant, null, 10, default));
        Assert.Empty(await repository.ListNodeTypesAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task APlainUnitCompanyAndPost_KeepTheirOldMeaning()
    {
        var name = Guid.NewGuid().ToString();
        var units = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("unit-" + name).Options);
        var unit = new OrganizationUnit(Guid.NewGuid(), Tenant, "معاونت");
        units.OrganizationUnits.Add(unit);
        await units.SaveChangesAsync();
        var stored = await units.OrganizationUnits.SingleAsync();
        Assert.Null(stored.ParentUnitId);
        Assert.Null(stored.NodeTypeId);
        Assert.Null(stored.ValidFrom);
        Assert.Null(stored.ValidTo);

        var db = Db(name);
        var company = new Company(Guid.NewGuid(), Tenant, "شرکت", "C1", null, null);
        var post = new UnitPosition(Guid.NewGuid(), Tenant, company.Id, null, Guid.NewGuid(), 0, "1");
        db.AddRange(company, post);
        await db.SaveChangesAsync();
        var c = await db.Companies.SingleAsync();
        Assert.Null(c.ParentCompanyId);
        Assert.Null(c.TemplateId);
        Assert.Equal(OrgUiLevel.Normal, c.UiLevel);
        var p = await db.UnitPositions.SingleAsync();
        Assert.False(p.IsManagerPost);
        Assert.False(p.IsCompanyHead);
    }

    [Fact]
    public void TheStructureFields_CanBeSet()
    {
        var unit = new OrganizationUnit(Guid.NewGuid(), Tenant, "اداره");
        var parent = Guid.NewGuid();
        var type = Guid.NewGuid();
        unit.SetParent(parent);
        unit.SetNodeType(type);
        unit.SetValidity(DateTimeOffset.UnixEpoch, null);
        Assert.Equal((parent, type), (unit.ParentUnitId, unit.NodeTypeId));
        Assert.Equal(DateTimeOffset.UnixEpoch, unit.ValidFrom);
        unit.SetParent(null);
        Assert.Null(unit.ParentUnitId);
    }
}
