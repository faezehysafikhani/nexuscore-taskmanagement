using Chat.Domain.Entities;
using Chat.Domain.Enums;
using Chat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Domain;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure.Infrastructure;
using Nexus.TaskManagement.Infrastructure;
using Notifications.Domain.Entities;
using Notifications.Infrastructure.Persistence;
using NexusCore.Application.Security;
using NexusCore.Domain.Auditing;
using NexusCore.Domain.Identity;
using NexusCore.Domain.Settings;
using NexusCore.Infrastructure.Persistence;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>
/// The organization condition under every query (global query filters): a query that forgets
/// <c>TenantId ==</c> still returns only the signed-in user's own organization.
/// </summary>
public sealed class TenantFilterTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private sealed class Scope(Guid? tenant) : ITenantScope
    {
        public Guid? FilterTenantIdFor(string contextName) => tenant;
    }

    private static DbContextOptions<T> Options<T>(string database) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseInMemoryDatabase(database).Options;

    // Only the model is read, so nothing connects: the SQL Server provider builds the real model.
    private static DbContextOptions<T> ModelOnly<T>() where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSqlServer("Server=.;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options;

    // ------------------------------------------------------------------ the safety net

    [Fact]
    public void EveryEntityWithATenantId_HasATenantFilter_InEveryContext()
    {
        var contexts = new DbContext[]
        {
            new NexusCoreDbContext(ModelOnly<NexusCoreDbContext>(), new Scope(TenantA)),
            new OrganizationDbContext(ModelOnly<OrganizationDbContext>(), new Scope(TenantA)),
            new OrgStructureDbContext(ModelOnly<OrgStructureDbContext>(), new Scope(TenantA)),
            new TaskManagementDbContext(ModelOnly<TaskManagementDbContext>(), null, new Scope(TenantA)),
            new ChatDbContext(ModelOnly<ChatDbContext>(), new Scope(TenantA)),
            new NotificationDbContext(ModelOnly<NotificationDbContext>(), new Scope(TenantA)),
        };

        // Shared on purpose, and said so: the audit log keeps sign-in attempts that belong to no
        // organization (it is read with an explicit tenant by the viewers).
        var shared = new HashSet<Type> { typeof(AuditLog) };

        foreach (var context in contexts)
        {
            using (context)
            {
                List<string> missing;
                try
                {
                    missing = TenantQueryFilters.UnfilteredTenantEntities(context).Where(type => !shared.Contains(type)).Select(type => type.Name).ToList();
                }
                catch (InvalidOperationException ex)
                {
                    throw new InvalidOperationException($"{context.GetType().Name}: {ex.Message}", ex);
                }

                Assert.True(missing.Count == 0, $"{context.GetType().Name}: no tenant filter on {string.Join(", ", missing)}. Add the filter, or list the entity as shared on purpose.");
            }
        }
    }

    // ------------------------------------------------------------------ the behaviour

    [Fact]
    public async Task Organizations_AreSeparated_EvenWhenTheQueryForgetsTheTenant()
    {
        const string database = "org";
        await using (var seed = new OrganizationDbContext(Options<OrganizationDbContext>(database)))
        {
            seed.OrganizationUnits.AddRange(
                new OrganizationUnit(Guid.NewGuid(), TenantA, "A unit"),
                new OrganizationUnit(Guid.NewGuid(), TenantB, "B unit"));
            await seed.SaveChangesAsync();
        }

        await using var asA = new OrganizationDbContext(Options<OrganizationDbContext>(database), new Scope(TenantA));
        var names = await asA.OrganizationUnits.Select(unit => unit.Name).ToListAsync();
        Assert.Equal(["A unit"], names);

        // No tenant to confine to (background work, sign-in, a caller allowed across organizations): everything.
        await using var unconfined = new OrganizationDbContext(Options<OrganizationDbContext>(database), new Scope(null));
        Assert.Equal(2, await unconfined.OrganizationUnits.CountAsync());
        await using var noScope = new OrganizationDbContext(Options<OrganizationDbContext>(database));
        Assert.Equal(2, await noScope.OrganizationUnits.CountAsync());
    }

    [Fact]
    public async Task UsersRolesAndSettings_FollowTheTenant_AndSystemWideSettingsStayVisible()
    {
        const string database = "core";
        await using (var seed = new NexusCoreDbContext(Options<NexusCoreDbContext>(database)))
        {
            seed.Users.AddRange(new User(Guid.NewGuid(), TenantA, null, "Ann", "x"), new User(Guid.NewGuid(), TenantB, null, "Bob", "x"));
            seed.Roles.AddRange(new Role(Guid.NewGuid(), TenantA, "RoleA"), new Role(Guid.NewGuid(), TenantB, "RoleB"));
            seed.Settings.AddRange(
                new SystemSetting(Guid.NewGuid(), TenantA, "K", "a", "System"),
                new SystemSetting(Guid.NewGuid(), TenantB, "K", "b", "System"),
                new SystemSetting(Guid.NewGuid(), null, "K", "system", "System"));
            seed.AuditLogs.AddRange(
                new AuditLog(Guid.NewGuid(), TenantB, null, "x", null, null, null, null),
                new AuditLog(Guid.NewGuid(), null, null, "identity.login_failed", null, null, null, null));
            await seed.SaveChangesAsync();
        }

        await using var asA = new NexusCoreDbContext(Options<NexusCoreDbContext>(database), new Scope(TenantA));
        Assert.Equal(["Ann"], await asA.Users.Select(user => user.DisplayName).ToListAsync());
        Assert.Equal(["RoleA"], await asA.Roles.Select(role => role.Name).ToListAsync());
        Assert.Equal(["a", "system"], (await asA.Settings.Select(setting => setting.Value).ToListAsync()).Order().ToList());
        // The audit log is read with an explicit tenant by its viewers; the filter leaves it alone.
        Assert.Equal(2, await asA.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Conversations_AndNotifications_FollowTheTenant()
    {
        const string chat = "chat";
        await using (var seed = new ChatDbContext(Options<ChatDbContext>(chat)))
        {
            seed.Conversations.AddRange(
                new Conversation(Guid.NewGuid(), TenantA, "A group", ChatType.Group, null),
                new Conversation(Guid.NewGuid(), TenantB, "B group", ChatType.Group, null));
            await seed.SaveChangesAsync();
        }

        await using (var asA = new ChatDbContext(Options<ChatDbContext>(chat), new Scope(TenantA)))
        {
            Assert.Equal(["A group"], await asA.Conversations.Select(conversation => conversation.Title!).ToListAsync());
        }

        const string notifications = "notifications";
        await using (var seed = new NotificationDbContext(Options<NotificationDbContext>(notifications)))
        {
            seed.Notifications.AddRange(
                new Notification(Guid.NewGuid(), TenantA, Guid.NewGuid(), "for A", "m", "t"),
                new Notification(Guid.NewGuid(), TenantB, Guid.NewGuid(), "for B", "m", "t"));
            await seed.SaveChangesAsync();
        }

        await using var notificationsAsB = new NotificationDbContext(Options<NotificationDbContext>(notifications), new Scope(TenantB));
        Assert.Single(await notificationsAsB.Notifications.ToListAsync());
    }
}
