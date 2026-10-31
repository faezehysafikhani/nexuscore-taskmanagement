using Microsoft.EntityFrameworkCore;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Domain;
using Nexus.OrgStructure.Infrastructure;

namespace Nexus.CompositionTests;

/// <summary>The simple chart's kinds (org / unit / position): what may sit under what, who holds people, and the stored people and heads.</summary>
public sealed class OrgNodeKindsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Theory]
    [InlineData(null, "unit", true)]
    [InlineData(null, "position", true)]
    [InlineData("unit", "unit", true)]
    [InlineData("unit", "position", true)]
    [InlineData("position", "position", true)]
    [InlineData("position", "unit", false)]
    [InlineData("other", "unit", false)]
    public void CanContain_FollowsTheKindTable(string? parent, string child, bool allowed) =>
        Assert.Equal(allowed, OrgNodeKinds.CanContain(parent, child));

    [Fact]
    public void Pairs_AreExactlyTheAllowedOnes()
    {
        var all = new string?[] { null, OrgNodeKinds.Unit, OrgNodeKinds.Position };
        var children = new[] { OrgNodeKinds.Unit, OrgNodeKinds.Position };
        var expected = all.SelectMany(parent => children.Where(child => OrgNodeKinds.CanContain(parent, child)).Select(child => (parent, child))).ToHashSet();
        Assert.Equal(expected, OrgNodeKinds.Pairs.ToHashSet());
        Assert.Equal(5, OrgNodeKinds.Pairs.Count);
    }

    [Fact]
    public void OnlyAPositionHoldsPeople_AndOnlyAUnitHasAHead()
    {
        Assert.True(OrgNodeKinds.HoldsPeople(OrgNodeKinds.Position));
        Assert.False(OrgNodeKinds.HoldsPeople(OrgNodeKinds.Unit));
        Assert.False(OrgNodeKinds.HoldsPeople(null));
        Assert.True(OrgNodeKinds.CanHaveHead(OrgNodeKinds.Unit));
        Assert.False(OrgNodeKinds.CanHaveHead(OrgNodeKinds.Position));
    }

    [Theory]
    [InlineData("kind_unit", "unit")]
    [InlineData("org_post", "unit")]
    [InlineData("kind_position", "position")]
    [InlineData("org_role", "position")]
    [InlineData("committee", null)]
    [InlineData(null, null)]
    public void KindOfTypeCode_RecognisesBothGenerationsOfCodes(string? code, string? kind) =>
        Assert.Equal(kind, OrgNodeKinds.KindOfTypeCode(code));

    [Fact]
    public async Task PeopleAndHeads_AreStoredAndReadBack_ManyPeopleInOnePosition_OnePersonInManyPositions()
    {
        var db = new OrgStructureDbContext(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("kinds-" + Guid.NewGuid()).Options);
        var repository = new OrgStructureRepository(db);
        var unit = Guid.NewGuid();
        var analyst = Guid.NewGuid();
        var accountant = Guid.NewGuid();
        var ali = Guid.NewGuid();
        var sara = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await repository.AddNodePersonAsync(new NodePerson(Guid.NewGuid(), Tenant, analyst, ali, isPrimary: true, now), default);
        await repository.AddNodePersonAsync(new NodePerson(Guid.NewGuid(), Tenant, analyst, sara, isPrimary: false, now.AddMinutes(1)), default);
        await repository.AddNodePersonAsync(new NodePerson(Guid.NewGuid(), Tenant, accountant, ali, isPrimary: true, now.AddMinutes(2)), default);
        await repository.AddNodeHeadAsync(new NodeHead(Guid.NewGuid(), Tenant, unit, analyst), default);
        await db.SaveChangesAsync();

        var people = await repository.ListNodePersonsAsync(Tenant, default);
        Assert.Equal(3, people.Count);
        Assert.Equal([ali, sara, ali], people.Select(p => p.UserId));
        Assert.Single(people, p => p.UnitId == analyst && p.IsPrimary);
        var heads = await repository.ListNodeHeadsAsync(Tenant, default);
        Assert.Equal(analyst, Assert.Single(heads).HeadUnitId);

        // The official one moves; the head changes.
        var saraRow = people.Single(p => p.UserId == sara);
        foreach (var row in people.Where(p => p.UnitId == analyst)) row.SetPrimary(row == saraRow);
        heads.Single().SetHead(accountant);
        await db.SaveChangesAsync();
        Assert.Equal(sara, (await repository.ListNodePersonsAsync(Tenant, default)).Single(p => p.UnitId == analyst && p.IsPrimary).UserId);
        Assert.Equal(accountant, (await repository.ListNodeHeadsAsync(Tenant, default)).Single().HeadUnitId);
    }
}
