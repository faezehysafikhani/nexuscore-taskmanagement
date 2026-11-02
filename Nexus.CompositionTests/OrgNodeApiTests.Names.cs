using Nexus.OrgStructure.Application.Dtos;

namespace Nexus.CompositionTests;

/// <summary>Two nodes beside each other may not have the same name (made, renamed or moved there); elsewhere they may.</summary>
public sealed partial class OrgNodeApiTests
{
    [Fact]
    public async Task NodesBesideEachOther_HaveDifferentNames()
    {
        var w = await World.BankAsync();
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        var finance = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت مالی", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        Assert.Equal("conflict", (await w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت  فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default)).Error.Code);
        Assert.Equal("conflict", (await w.Nodes.UpdateNodeAsync(Tenant, finance.Id, new UpdateNodeRequest("معاونت فناوری"), default)).Error.Code);

        // The same name under different parents, or in another company: fine. Moved beside its namesake: refused.
        var a = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل پشتیبانی", ParentNodeId: it.Id, NodeTypeId: w.Type("gd")), default));
        await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("اداره کل پشتیبانی", ParentNodeId: finance.Id, NodeTypeId: w.Type("gd")), default));
        var other = await w.CompanyAsync("C2");
        await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: other, NodeTypeId: w.Type("deputy")), default));
        Assert.Equal("conflict", (await w.Nodes.MoveNodeAsync(Tenant, a.Id, new MoveNodeRequest(finance.Id), default)).Error.Code);

        // Renaming to its own name (or only its spacing) is not a clash with itself.
        await w.Ok(w.Nodes.UpdateNodeAsync(Tenant, it.Id, new UpdateNodeRequest("معاونت فناوری"), default));
    }
}
