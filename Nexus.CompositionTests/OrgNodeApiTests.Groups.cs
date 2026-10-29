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
        var bank = await w.Ok(w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("بانک", "BK", null, ParentCompanyId: holding), default));
        Assert.Equal(holding, bank.ParentCompanyId);
        var insurer = await w.CompanyAsync("INS");
        Assert.Null((await w.Ok(w.Service.ListCompaniesAsync(Tenant, default))).Single(c => c.Id == insurer).ParentCompanyId);
        Assert.Equal("validation.error", (await w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("x", "X", null, ParentCompanyId: Guid.NewGuid()), default)).Error.Code);

        var leasing = await w.Ok(w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("لیزینگ", "LS", null, OrgNodeService.SystemTemplateId("simple"), bank.Id), default));
        Assert.Equal(bank.Id, leasing.ParentCompanyId);

        // Loops: the holding under its grandchild, a company under itself.
        Assert.Equal("conflict", (await groups.SetParentCompanyAsync(Tenant, holding, new SetParentCompanyRequest(leasing.Id), default)).Error.Code);
        Assert.Equal("conflict", (await groups.SetParentCompanyAsync(Tenant, bank.Id, new SetParentCompanyRequest(bank.Id), default)).Error.Code);

        var tree = await w.Ok(groups.GetCompanyGroupsAsync(Tenant, default));
        Assert.Equal(["شرکت H", "شرکت INS"], tree.Select(g => g.Name));
        Assert.Equal("لیزینگ", tree[0].Children.Single().Children.Single().Name);

        // Out of the group again: a company of its own; its people and structure untouched.
        Assert.Null((await w.Ok(groups.SetParentCompanyAsync(Tenant, bank.Id, new SetParentCompanyRequest(null), default))).ParentCompanyId);
        Assert.Equal(3, (await w.Ok(groups.GetCompanyGroupsAsync(Tenant, default))).Count);
    }
}
