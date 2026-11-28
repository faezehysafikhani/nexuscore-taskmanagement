using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;

namespace Nexus.CompositionTests;

/// <summary>Phase 6.3: a company's parent company (no loops), the group view, and a company with none as before.</summary>
public sealed partial class OrgNodeApiTests
{
    [Fact]
    public async Task ACompanyMaySitUnderAnother_NeverInALoop_AndOneWithNoneIsAsBefore()
    {
        var w = new World();
        var groups = (IOrgCompanyGroupService)w.Service;
        var holding = await w.CompanyAsync("H");
        // The system holds one company, so a service refuses another (with a parent or without); a group of
        // companies, as older data may hold, is stored as data here.
        Assert.Equal("conflict", (await w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("بانک", "BK", null, ParentCompanyId: holding), default)).Error.Code);
        var bank = await w.CompanyAsync("BK");
        Assert.Equal(holding, (await w.Ok(groups.SetParentCompanyAsync(Tenant, bank, new SetParentCompanyRequest(holding), default))).ParentCompanyId);
        var insurer = await w.CompanyAsync("INS");
        Assert.Null((await w.Ok(w.Service.ListCompaniesAsync(Tenant, default))).Single(c => c.Id == insurer).ParentCompanyId);

        var leasing = await w.CompanyAsync("LS");
        Assert.Equal(bank, (await w.Ok(groups.SetParentCompanyAsync(Tenant, leasing, new SetParentCompanyRequest(bank), default))).ParentCompanyId);

        // Loops: the holding under its grandchild, a company under itself.
        Assert.Equal("conflict", (await groups.SetParentCompanyAsync(Tenant, holding, new SetParentCompanyRequest(leasing), default)).Error.Code);
        Assert.Equal("conflict", (await groups.SetParentCompanyAsync(Tenant, bank, new SetParentCompanyRequest(bank), default)).Error.Code);

        var tree = await w.Ok(groups.GetCompanyGroupsAsync(Tenant, default));
        Assert.Equal(["شرکت H", "شرکت INS"], tree.Select(g => g.Name));
        Assert.Equal("شرکت LS", tree[0].Children.Single().Children.Single().Name);

        // Out of the group again: a company of its own; its people and structure untouched.
        Assert.Null((await w.Ok(groups.SetParentCompanyAsync(Tenant, bank, new SetParentCompanyRequest(null), default))).ParentCompanyId);
        Assert.Equal(3, (await w.Ok(groups.GetCompanyGroupsAsync(Tenant, default))).Count);
    }
}
