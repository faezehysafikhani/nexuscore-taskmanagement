using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Domain;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Domain;
using Nexus.OrgStructure.Infrastructure;

namespace Nexus.CompositionTests;

/// <summary>The one graph of units (1.3): a company's units with no parent, or with the company as parent, are the same flat structure.</summary>
public sealed class OrgGraphTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Tech = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid Finance = Guid.NewGuid();
    private static readonly Guid TechManager = Guid.NewGuid();
    private static readonly Guid OfficeManager = Guid.NewGuid();
    private static readonly Guid Head = Guid.NewGuid();
    private static readonly Guid Expert = Guid.NewGuid();

    private static Dictionary<Guid, Guid> Links(params Guid[] units) => units.ToDictionary(unit => unit, _ => Company);

    private static OrgGraph Flat(Guid? parentOfAll) => new(
        [new UnitNode(Tech, parentOfAll, TechManager), new UnitNode(Office, parentOfAll, OfficeManager), new UnitNode(Finance, parentOfAll, null)],
        Links(Tech, Office, Finance));

    private static OrgGraph Nested(bool officeIsBoundary = true) => new(
        [new UnitNode(Tech, null, TechManager), new UnitNode(Office, Tech, OfficeManager, officeIsBoundary), new UnitNode(Finance, null, null)],
        Links(Tech, Finance));

    [Fact]
    public void NoParent_AndTheCompanyAsParent_AreTheSameFlatGraph()
    {
        foreach (var graph in new[] { Flat(null), Flat(Company) })
        {
            foreach (var unit in new[] { Tech, Office, Finance })
            {
                Assert.Null(graph.ParentOf(unit));
                Assert.Equal([unit], graph.SubtreeOf(unit));
                Assert.Equal(unit, graph.DomainOf(unit));
                Assert.Equal(Company, graph.CompanyOfUnit(unit));
                Assert.Equal(unit, graph.LinkedAncestorOf(unit));
            }
        }
    }

    [Fact]
    public void NoParent_AndTheCompanyAsParent_GiveTheSameManagers()
    {
        string Managers(OrgGraph graph)
        {
            var resolver = new HierarchyResolver(
                graph.Nodes, Links(Tech, Office, Finance), new Dictionary<Guid, Guid?> { [Company] = Head },
                [new AssignmentNode(Expert, Company, Office, null), new AssignmentNode(TechManager, Company, Tech, null), new AssignmentNode(Head, Company, null, null)]);
            return string.Join("|", new[] { Expert, TechManager, Head }.Select(user => $"{resolver.ManagerOf(user)?.ManagerUserId}:{resolver.ManagerOf(user)?.Source}"));
        }

        Assert.Equal(Managers(Flat(null)), Managers(Flat(Company)));
    }

    [Fact]
    public void ANestedUnit_HasItsParent_ItsSubtree_AndTheCompanyOfItsTopUnit()
    {
        var graph = Nested();
        Assert.Equal(Tech, graph.ParentOf(Office));
        Assert.Equal([Office], graph.ChildrenOf(Tech));
        Assert.Equal(new HashSet<Guid> { Tech, Office }, graph.SubtreeOf(Tech).ToHashSet());
        Assert.Equal([Office], graph.SubtreeOf(Office));
        Assert.Equal(Company, graph.CompanyOfUnit(Office));
        Assert.Equal(Tech, graph.LinkedAncestorOf(Office));
        Assert.Null(graph.CompanyOfUnit(Guid.NewGuid()));
    }

    [Fact]
    public void TheSameDeputy_IsTheNearestDomainBoundary()
    {
        Assert.Equal(Office, Nested(officeIsBoundary: true).DomainOf(Office));
        Assert.Equal(Tech, Nested(officeIsBoundary: false).DomainOf(Office));
        Assert.Equal(Tech, Nested(officeIsBoundary: false).DomainOf(Tech));
        // no boundary at all up the line: the unit is its own domain
        var none = new OrgGraph([new UnitNode(Tech, null, null, false), new UnitNode(Office, Tech, null, false)], Links(Tech));
        Assert.Equal(Office, none.DomainOf(Office));
        Assert.Equal(Finance, none.DomainOf(Finance));
    }

    [Fact]
    public void ANestedUnit_ChangesTheManager_OnlyWhereTheDataSaysSo()
    {
        Guid? ManagerOfExpert(OrgGraph graph, Guid? officeManager)
        {
            var nodes = graph.Nodes.Select(n => n.Id == Office ? n with { ManagerUserId = officeManager } : n);
            return new HierarchyResolver(nodes, Links(Tech, Finance), new Dictionary<Guid, Guid?> { [Company] = Head },
                [new AssignmentNode(Expert, Company, Office, null)]).ManagerOf(Expert)?.ManagerUserId;
        }

        // the office has its own manager: that is the expert's manager, nested or not
        Assert.Equal(OfficeManager, ManagerOfExpert(Nested(), OfficeManager));
        // the office has none: nested, the deputy's manager above it; flat, the company head (as before)
        Assert.Equal(TechManager, ManagerOfExpert(Nested(), null));
        Assert.Equal(Head, ManagerOfExpert(Flat(null), null));
    }

    [Fact]
    public void DataThatLoops_NeverHangs()
    {
        var loop = new OrgGraph([new UnitNode(Tech, Office, null), new UnitNode(Office, Tech, null)], Links(Finance));
        Assert.Equal(new HashSet<Guid> { Tech, Office }, loop.SubtreeOf(Tech).ToHashSet());
        Assert.Null(loop.CompanyOfUnit(Tech));
        Assert.Equal(Tech, loop.DomainOf(Tech));
    }

    [Fact]
    public async Task TheProvider_ReadsUnitsLinksAndNodeTypes_IntoOneGraph()
    {
        var name = Guid.NewGuid().ToString();
        var organization = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("graph-o-" + name).Options);
        var structure = new OrgStructureDbContext(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("graph-s-" + name).Options);
        var committee = new NodeType(Guid.NewGuid(), Tenant, "committee", "کمیته", isDomainBoundary: false);
        var tech = new OrganizationUnit(Tech, Tenant, "معاونت");
        var office = new OrganizationUnit(Office, Tenant, "اداره");
        office.SetParent(Tech);
        office.SetNodeType(committee.Id);
        organization.OrganizationUnits.AddRange(tech, office);
        structure.AddRange(committee, new CompanyUnit(Guid.NewGuid(), Tenant, Company, Tech));
        await organization.SaveChangesAsync();
        await structure.SaveChangesAsync();

        var graph = await new OrgGraphProvider(new OrganizationUnitRepository(organization), new OrgStructureRepository(structure)).GetAsync(Tenant, default);
        Assert.Equal(Tech, graph.ParentOf(Office));
        Assert.Equal(Company, graph.CompanyOfUnit(Office));
        Assert.Equal(Tech, graph.DomainOf(Office));
        Assert.Equal(Tech, graph.DomainOf(Tech));
    }
}
