using Microsoft.EntityFrameworkCore;
using Nexus.OrgStructure.Domain;
using Nexus.OrgStructure.Infrastructure;

namespace Nexus.CompositionTests;

/// <summary>What a user gets from the posts they hold: the position's and the post's own, by date, with stand-ins.</summary>
public sealed class PositionPermissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    private readonly Guid _tenant = Guid.NewGuid();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static OrgStructureDbContext NewContext() =>
        new(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("pos-perm-" + Guid.NewGuid()).Options);

    private Guid _company = Guid.NewGuid();

    private Position AddPosition(OrgStructureDbContext db, string title, bool active = true, params string[] permissions)
    {
        var position = new Position(Guid.NewGuid(), _tenant, title);
        if (!active)
        {
            position.Update(title, null, false);
        }

        db.Positions.Add(position);
        db.PositionCompanies.Add(new PositionCompany(Guid.NewGuid(), _tenant, position.Id, _company));
        db.PositionPermissions.AddRange(permissions.Select(name => new PositionPermission(_tenant, _company, position.Id, name)));
        return position;
    }

    private UnitPosition AddPost(OrgStructureDbContext db, Position position, string code, params string[] permissions)
    {
        var post = new UnitPosition(Guid.NewGuid(), _tenant, _company, Guid.NewGuid(), position.Id, 0, code);
        db.UnitPositions.Add(post);
        db.UnitPositionPermissions.AddRange(permissions.Select(name => new UnitPositionPermission(_tenant, post.Id, name)));
        return post;
    }

    private void Appoint(OrgStructureDbContext db, Guid userId, UnitPosition post, DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        var holding = new UserPosition(Guid.NewGuid(), _tenant, userId, post.Id, start);
        if (end is { } to)
        {
            holding.End(to);
        }

        db.UserPositions.Add(holding);
    }

    private static PositionPermissionProvider Provider(OrgStructureDbContext db) => new(db, new FixedClock(Now));

    [Fact]
    public async Task HolderGetsThePositionsAndThePostsPermissions_AndNothingWithoutAPost()
    {
        using var db = NewContext();
        var expert = AddPosition(db, "کارشناس", true, "tasks.view");
        var post = AddPost(db, expert, "P1", "reports.view");
        var user = Guid.NewGuid();
        Appoint(db, user, post);
        await db.SaveChangesAsync();

        Assert.Equal(["reports.view", "tasks.view"], (await Provider(db).GetPermissionNamesAsync(user, default)).Order());
        Assert.Empty(await Provider(db).GetPermissionNamesAsync(Guid.NewGuid(), default));
        var granting = await Provider(db).GetGrantingPositionsAsync(user, default);
        Assert.Contains("کارشناس", granting["tasks.view"].Single());
    }

    [Fact]
    public async Task SeveralPosts_AreUnioned_AndAnInactivePositionGivesNothing()
    {
        using var db = NewContext();
        var a = AddPosition(db, "الف", true, "tasks.view");
        var b = AddPosition(db, "ب", true, "reports.view");
        var off = AddPosition(db, "غیرفعال", false, "users.view");
        var user = Guid.NewGuid();
        Appoint(db, user, AddPost(db, a, "A"));
        Appoint(db, user, AddPost(db, b, "B"));
        Appoint(db, user, AddPost(db, off, "C"));
        await db.SaveChangesAsync();

        Assert.Equal(["reports.view", "tasks.view"], (await Provider(db).GetPermissionNamesAsync(user, default)).Order());
    }

    [Fact]
    public async Task AnAppointmentGivesAccessOnlyInItsPeriod()
    {
        using var db = NewContext();
        var position = AddPosition(db, "مدیر", true, "tasks.view");
        var future = Guid.NewGuid();
        var ended = Guid.NewGuid();
        var current = Guid.NewGuid();
        Appoint(db, future, AddPost(db, position, "F"), start: Now.AddDays(1));
        Appoint(db, ended, AddPost(db, position, "E"), start: Now.AddDays(-9), end: Now.AddDays(-1));
        Appoint(db, current, AddPost(db, position, "C"), start: Now.AddDays(-1), end: Now.AddDays(1));
        await db.SaveChangesAsync();

        Assert.Empty(await Provider(db).GetPermissionNamesAsync(future, default));
        Assert.Empty(await Provider(db).GetPermissionNamesAsync(ended, default));
        Assert.Equal(["tasks.view"], await Provider(db).GetPermissionNamesAsync(current, default));
    }

    [Fact]
    public async Task AStandIn_GetsTheVacantPostsPermissions_WhileItIsOn()
    {
        using var db = NewContext();
        var head = AddPosition(db, "رئیس", true, "reports.view");
        var deputy = AddPosition(db, "قائم‌مقام", true, "tasks.view");
        var vacant = AddPost(db, head, "H");
        var standInPost = AddPost(db, deputy, "D");
        var standIn = Guid.NewGuid();
        Appoint(db, standIn, standInPost);
        db.PostActings.Add(new PostActing(Guid.NewGuid(), _tenant, vacant.Id, standInPost.Id));
        await db.SaveChangesAsync();

        // The vacant post has no holder: the stand-in has its permissions too, and it is said so.
        Assert.Equal(["reports.view", "tasks.view"], (await Provider(db).GetPermissionNamesAsync(standIn, default)).Order());
        Assert.StartsWith("جانشین", (await Provider(db).GetGrantingPositionsAsync(standIn, default))["reports.view"].Single());

        // Someone takes the vacant post: the stand-in is over.
        Appoint(db, Guid.NewGuid(), vacant);
        await db.SaveChangesAsync();
        Assert.Equal(["tasks.view"], await Provider(db).GetPermissionNamesAsync(standIn, default));
    }

    [Fact]
    public async Task APositionsPermissions_AreSeparatePerCompany()
    {
        using var db = NewContext();
        var first = _company;
        var expert = AddPosition(db, "کارشناس", true, "tasks.view");
        var inFirst = Guid.NewGuid();
        Appoint(db, inFirst, AddPost(db, expert, "A"));

        // The same position in another company: its own list (here a different permission).
        _company = Guid.NewGuid();
        db.PositionCompanies.Add(new PositionCompany(Guid.NewGuid(), _tenant, expert.Id, _company));
        db.PositionPermissions.Add(new PositionPermission(_tenant, _company, expert.Id, "reports.view"));
        var inSecond = Guid.NewGuid();
        Appoint(db, inSecond, AddPost(db, expert, "B"));
        await db.SaveChangesAsync();

        Assert.Equal(["tasks.view"], await Provider(db).GetPermissionNamesAsync(inFirst, default));
        Assert.Equal(["reports.view"], await Provider(db).GetPermissionNamesAsync(inSecond, default));
        Assert.NotEqual(first, _company);
    }

    [Fact]
    public async Task ARowLeftBehind_NeverCounts_AndRemovingThePositionFromTheCompanyClearsItsPermissions()
    {
        using var db = NewContext();
        var expert = AddPosition(db, "کارشناس", true, "tasks.view");
        var user = Guid.NewGuid();
        Appoint(db, user, AddPost(db, expert, "A"));
        await db.SaveChangesAsync();
        Assert.Equal(["tasks.view"], await Provider(db).GetPermissionNamesAsync(user, default));

        // Through the repository: the position leaves the company, the permissions go with it...
        var repository = new OrgStructureRepository(db);
        var link = await db.PositionCompanies.SingleAsync();
        repository.RemovePositionCompany(link);
        await db.SaveChangesAsync();
        Assert.Empty(await db.PositionPermissions.ToListAsync());
        Assert.Empty(await Provider(db).GetPermissionNamesAsync(user, default));

        // ...and are not back when the position returns to the company.
        db.PositionCompanies.Add(new PositionCompany(Guid.NewGuid(), _tenant, expert.Id, _company));
        await db.SaveChangesAsync();
        Assert.Empty(await Provider(db).GetPermissionNamesAsync(user, default));
    }

    [Fact]
    public async Task ALeftoverRow_ForAPositionNoLongerInTheCompany_GivesNothing()
    {
        using var db = NewContext();
        var expert = AddPosition(db, "کارشناس", true, "tasks.view");
        var user = Guid.NewGuid();
        Appoint(db, user, AddPost(db, expert, "A"));
        await db.SaveChangesAsync();
        db.PositionCompanies.RemoveRange(await db.PositionCompanies.ToListAsync());   // the link is gone, the permission row is not
        await db.SaveChangesAsync();

        Assert.Empty(await Provider(db).GetPermissionNamesAsync(user, default));
    }

    [Fact]
    public async Task RemovingAPostOrAPosition_ClearsWhatItGave()
    {
        using var db = NewContext();
        var expert = AddPosition(db, "کارشناس", true, "tasks.view");
        var post = AddPost(db, expert, "A", "reports.view");
        await db.SaveChangesAsync();
        var repository = new OrgStructureRepository(db);

        repository.RemoveUnitPosition(post);
        await db.SaveChangesAsync();
        Assert.Empty(await db.UnitPositionPermissions.ToListAsync());

        repository.RemovePosition(expert);
        await db.SaveChangesAsync();
        Assert.Empty(await db.PositionPermissions.ToListAsync());
    }
}
