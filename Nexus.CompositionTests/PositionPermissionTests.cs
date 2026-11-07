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

    private Position AddPosition(OrgStructureDbContext db, string title, bool active = true, params string[] permissions)
    {
        var position = new Position(Guid.NewGuid(), _tenant, title);
        if (!active)
        {
            position.Update(title, null, false);
        }

        db.Positions.Add(position);
        db.PositionPermissions.AddRange(permissions.Select(name => new PositionPermission(_tenant, position.Id, name)));
        return position;
    }

    private UnitPosition AddPost(OrgStructureDbContext db, Position position, string code, params string[] permissions)
    {
        var post = new UnitPosition(Guid.NewGuid(), _tenant, Guid.NewGuid(), Guid.NewGuid(), position.Id, 0, code);
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
}
