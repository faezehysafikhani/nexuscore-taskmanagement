using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Domain;
using Nexus.OrgStructure.Infrastructure;
using NexusCore.Application.Approvals;

namespace Nexus.CompositionTests;

/// <summary>Workflow approvers and unit heads read from the organization chart: posts, positions, stand-ins.</summary>
public sealed class OrgApprovalKeyTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _company = Guid.NewGuid();

    private sealed class NoUnits : IOrganizationUnitRepository
    {
        public Task<OrganizationUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<OrganizationUnit?>(null);
        public Task<IReadOnlyList<OrganizationUnit>> ListAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OrganizationUnit>>([]);
        public Task AddAsync(OrganizationUnit unit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static OrgStructureDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("org-keys-" + Guid.NewGuid()).Options);

    private Position AddPosition(OrgStructureDbContext db, string title)
    {
        var position = new Position(Guid.NewGuid(), _tenant, title);
        db.Positions.Add(position);
        return position;
    }

    private UnitPosition AddPost(OrgStructureDbContext db, Position position, Guid? unitId = null)
    {
        var post = new UnitPosition(Guid.NewGuid(), _tenant, _company, unitId ?? Guid.NewGuid(), position.Id, 0, "P" + Guid.NewGuid().ToString("N")[..6]);
        db.UnitPositions.Add(post);
        return post;
    }

    private void Appoint(OrgStructureDbContext db, Guid userId, UnitPosition post) =>
        db.UserPositions.Add(new UserPosition(Guid.NewGuid(), _tenant, userId, post.Id, DateTimeOffset.UtcNow.AddDays(-1)));

    private ApprovalSubject Subject => new("Action", Guid.NewGuid(), _tenant);

    [Fact]
    public async Task APostKey_IsItsHolder_AndWhileVacantItsStandIn()
    {
        using var db = NewContext();
        var head = AddPosition(db, "رئیس اداره");
        var deputy = AddPosition(db, "معاون اداره");
        var headPost = AddPost(db, head);
        var deputyPost = AddPost(db, deputy);
        var holder = Guid.NewGuid();
        var standIn = Guid.NewGuid();
        Appoint(db, standIn, deputyPost);
        db.PostActings.Add(new PostActing(Guid.NewGuid(), _tenant, headPost.Id, deputyPost.Id));
        await db.SaveChangesAsync();
        var resolver = new OrgApprovalKeyResolver(new OrgStructureRepository(db), new NoUnits());
        var key = ApprovalParticipants.PostPrefix + headPost.Id.ToString("N");

        Assert.Equal(standIn, await resolver.ResolveAsync(Subject, null, key, default));

        Appoint(db, holder, headPost);
        await db.SaveChangesAsync();
        Assert.Equal(holder, await resolver.ResolveAsync(Subject, null, key, default));
        Assert.Null(await resolver.ResolveAsync(Subject, null, ApprovalParticipants.PostPrefix + Guid.NewGuid(), default));
    }

    [Fact]
    public async Task APositionKey_IsItsOnlyHolder_AndNobodyWhenSeveralAndNoneAbove()
    {
        using var db = NewContext();
        var finance = AddPosition(db, "مدیر مالی");
        var expert = AddPosition(db, "کارشناس");
        var cfo = Guid.NewGuid();
        Appoint(db, cfo, AddPost(db, finance));
        Appoint(db, Guid.NewGuid(), AddPost(db, expert));
        Appoint(db, Guid.NewGuid(), AddPost(db, expert));
        await db.SaveChangesAsync();
        var resolver = new OrgApprovalKeyResolver(new OrgStructureRepository(db), new NoUnits());

        Assert.Equal(cfo, await resolver.ResolveAsync(Subject, null, ApprovalParticipants.PositionPrefix + finance.Id.ToString("N"), default));
        Assert.Null(await resolver.ResolveAsync(Subject, null, ApprovalParticipants.PositionPrefix + expert.Id.ToString("N"), default));
        Assert.Null(await resolver.ResolveAsync(Subject, null, "Something", default));
    }

    [Fact]
    public async Task UnitHeads_AreTheHoldersOfAManagerPost_AndTheHeadOfACompanyHeadsItsUnits()
    {
        using var db = NewContext();
        var unit = Guid.NewGuid();
        var topUnit = Guid.NewGuid();
        var managerPost = AddPost(db, AddPosition(db, "رئیس"), unit);
        managerPost.SetManagerPost(true);
        var plainPost = AddPost(db, AddPosition(db, "کارشناس"), unit);
        var headPost = AddPost(db, AddPosition(db, "مدیرعامل"), null);
        headPost.SetCompanyHead(true);
        db.CompanyUnits.Add(new CompanyUnit(Guid.NewGuid(), _tenant, _company, topUnit));
        var boss = Guid.NewGuid();
        var expert = Guid.NewGuid();
        var ceo = Guid.NewGuid();
        Appoint(db, boss, managerPost);
        Appoint(db, expert, plainPost);
        Appoint(db, ceo, headPost);
        await db.SaveChangesAsync();
        var heads = new OrgUnitHeads(new OrgStructureRepository(db));

        Assert.Equal([unit], await heads.UnitsHeadedByAsync(_tenant, boss, default));
        Assert.Empty(await heads.UnitsHeadedByAsync(_tenant, expert, default));
        Assert.Equal([topUnit], await heads.UnitsHeadedByAsync(_tenant, ceo, default));
    }
}
