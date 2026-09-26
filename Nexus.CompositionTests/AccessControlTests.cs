using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Nexus.Integrations.TaskNotifications;
using Nexus.TaskManagement;
using Nexus.TaskManagement.Endpoints;
using Nexus.TaskManagement.Infrastructure;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application;
using NexusCore.Application.Endpoints;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Messaging;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.Infrastructure;
using NexusCore.Infrastructure.Persistence;
using NexusCore.Infrastructure.Security;
using Ticketing.Api.Endpoints;
using Ticketing.Application;
using Ticketing.Application.Common.Security;
using Ticketing.Infrastructure;
using Ticketing.Infrastructure.Persistence;

namespace Nexus.CompositionTests;

/// <summary>
/// Permissions and resource-level access through the real HTTP endpoints of Identity,
/// TaskManagement and Ticketing, composed the way the Rozet host composes them (JWT bearer,
/// permission policies, the modules' own DI), on in-memory databases.
///
/// Two organizations: A (administrator, members, a read-only user) and B (its own
/// administrator and member). Every test creates the users and data it changes.
/// </summary>
public sealed class AccessControlTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] MemberPermissions =
    [
        TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit,
        TaskManagementPermissions.Delete, TaskManagementPermissions.Assign, TaskManagementPermissions.ManageRecurring,
        TaskManagementPermissions.ManageTags, TaskManagementPermissions.UploadFiles, TaskManagementPermissions.Comment,
        TaskManagementPermissions.ManageNotes, IdentityPermissions.UsersView,
        TicketingPermissions.View, TicketingPermissions.Create
    ];

    // ---------------------------------------------------------------- permission per operation

    [Fact]
    public async Task CreateTask_NeedsTasksCreate()
    {
        var member = await host.UserAsync(host.TenantA, MemberPermissions);
        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);

        Assert.Equal(HttpStatusCode.OK, (await host.CreateTaskAsync(member, "Member's task")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.CreateTaskAsync(viewer, "Viewer's task")).StatusCode);
    }

    [Fact]
    public async Task ListUsers_NeedsUsersView()
    {
        var member = await host.UserAsync(host.TenantA, MemberPermissions);
        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(member, HttpMethod.Get, "/api/identity/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(viewer, HttpMethod.Get, "/api/identity/users")).StatusCode);
    }

    [Fact]
    public async Task EditAndDelete_NeedTheirPermissions_AndTheOwnership()
    {
        var owner = await host.UserAsync(host.TenantA, MemberPermissions);
        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        var colleague = await host.UserAsync(host.TenantA, MemberPermissions);
        var id = await host.CreatedTaskIdAsync(owner, "Owned");

        // Without Tasks.Edit / Tasks.Delete: refused by the policy, whatever the task.
        Assert.Equal(HttpStatusCode.Forbidden, (await host.UpdateTaskAsync(viewer, id, "Changed by viewer")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(viewer, HttpMethod.Delete, $"/api/task-management/tasks/{id}")).StatusCode);

        // With the permissions but not involved in the task: it does not exist for them.
        Assert.Equal(HttpStatusCode.NotFound, (await host.UpdateTaskAsync(colleague, id, "Changed by colleague")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(colleague, HttpMethod.Delete, $"/api/task-management/tasks/{id}")).StatusCode);

        // The owner may.
        Assert.Equal(HttpStatusCode.OK, (await host.UpdateTaskAsync(owner, id, "Changed by owner")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(owner, HttpMethod.Delete, $"/api/task-management/tasks/{id}")).StatusCode);
    }

    [Fact]
    public async Task AnAssignee_SeesAndWorksOnTheTask_ButDoesNotChangeOrDeleteIt()
    {
        var owner = await host.UserAsync(host.TenantA, MemberPermissions);
        var assignee = await host.UserAsync(host.TenantA, MemberPermissions);
        var id = await host.CreatedTaskIdAsync(owner, "Shared", assignedUserId: assignee.Id);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(assignee, HttpMethod.Get, $"/api/task-management/tasks/{id}")).StatusCode);
        Assert.Contains(id, await host.ListTaskIdsAsync(assignee));
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(assignee, HttpMethod.Patch, $"/api/task-management/tasks/{id}/status", new { status = "InProgress" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(assignee, HttpMethod.Post, $"/api/task-management/tasks/{id}/comments", new { text = "On it" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await host.UpdateTaskAsync(assignee, id, "Renamed by assignee")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(assignee, HttpMethod.Delete, $"/api/task-management/tasks/{id}")).StatusCode);
    }

    [Fact]
    public async Task ATeamMember_SeesTheTeamsTask_WhenOnItsAccessList()
    {
        var owner = await host.UserAsync(host.TenantA, MemberPermissions);
        var member = await host.UserAsync(host.TenantA, MemberPermissions);
        var teammateWithoutAccess = await host.UserAsync(host.TenantA, MemberPermissions);
        var outsider = await host.UserAsync(host.TenantA, MemberPermissions);
        var teamId = await host.TeamAsync(host.TenantA, member.Id, teammateWithoutAccess.Id);
        var created = await host.SendAsync(owner, HttpMethod.Post, "/api/task-management/tasks", new
        {
            title = "For the team", dueDate = "2030-01-01", priority = "Medium", assignedUserId = owner.Id,
            assignedUserGroupId = teamId, assigneeUserIds = new[] { member.Id },
        });
        var id = (await Json(created)).GetProperty("id").GetGuid();

        Assert.Contains(id, await host.ListTaskIdsAsync(member));
        // The team is the task's context, not a grant: only those on the access list see it.
        Assert.DoesNotContain(id, await host.ListTaskIdsAsync(teammateWithoutAccess));
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(teammateWithoutAccess, HttpMethod.Get, $"/api/task-management/tasks/{id}")).StatusCode);
        Assert.DoesNotContain(id, await host.ListTaskIdsAsync(outsider));
    }

    [Fact]
    public async Task ManageAll_SeesAndManagesEveryTaskOfItsOrganizationOnly()
    {
        var owner = await host.UserAsync(host.TenantA, MemberPermissions);
        var manager = await host.UserAsync(host.TenantA, [.. MemberPermissions, TaskManagementPermissions.ManageAll]);
        var otherOrgManager = await host.UserAsync(host.TenantB, [.. MemberPermissions, TaskManagementPermissions.ManageAll]);
        var id = await host.CreatedTaskIdAsync(owner, "Someone's");

        Assert.Contains(id, await host.ListTaskIdsAsync(manager));
        Assert.Equal(HttpStatusCode.OK, (await host.UpdateTaskAsync(manager, id, "Managed")).StatusCode);

        Assert.DoesNotContain(id, await host.ListTaskIdsAsync(otherOrgManager));
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(otherOrgManager, HttpMethod.Get, $"/api/task-management/tasks/{id}")).StatusCode);
    }

    // ---------------------------------------------------------------- changes on a live session

    [Fact]
    public async Task GrantingAndRevokingAPermission_AppliesToTheCurrentToken()
    {
        var viewer = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.CreateTaskAsync(viewer, "Before")).StatusCode);

        var grant = await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{viewer.Id}/permissions",
            new { permissionIds = await host.PermissionIdsAsync(TaskManagementPermissions.Create) });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.CreateTaskAsync(viewer, "After the grant")).StatusCode);

        var revoke = await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{viewer.Id}/permissions", new { permissionIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.CreateTaskAsync(viewer, "After the revoke")).StatusCode);
    }

    [Fact]
    public async Task RemovingTheRole_EndsWhatItAllowed_OnTheCurrentToken()
    {
        var member = await host.UserAsync(host.TenantA, MemberPermissions, viaRole: true);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(member, HttpMethod.Get, "/api/task-management/tasks")).StatusCode);

        var remove = await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/users/{member.Id}/roles", new { roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(member, HttpMethod.Get, "/api/task-management/tasks")).StatusCode);
    }

    [Fact]
    public async Task DisablingAUser_EndsTheirValidToken()
    {
        var member = await host.UserAsync(host.TenantA, MemberPermissions);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(member, HttpMethod.Get, "/api/task-management/tasks")).StatusCode);

        var disable = await host.SendAsync(host.Admin, HttpMethod.Patch, $"/api/identity/users/{member.Id}/status", new { isActive = false });

        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(member, HttpMethod.Get, "/api/task-management/tasks")).StatusCode);
    }

    [Fact]
    public async Task CallingAnAdministrationEndpointDirectly_WithoutThePermission_IsRefused()
    {
        var member = await host.UserAsync(host.TenantA, MemberPermissions);
        var target = await host.UserAsync(host.TenantA, MemberPermissions);

        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(member, HttpMethod.Delete, $"/api/identity/users/{target.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(member, HttpMethod.Patch, $"/api/identity/users/{target.Id}/status", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(member, HttpMethod.Put, $"/api/identity/users/{target.Id}/roles", new { roleIds = new[] { host.AdminRoleA } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(member, HttpMethod.Get, "/api/platform/audit-logs")).StatusCode);
    }

    // ---------------------------------------------------------------- escalation

    [Fact]
    public async Task NobodyHandsOutMoreThanTheyHave_AndTheAdministratorRoleIsFixed()
    {
        // May assign roles and edit role permissions, but holds nothing else.
        var roleManager = await host.UserAsync(host.TenantA,
        [
            IdentityPermissions.UsersAssignRoles, IdentityPermissions.RolesAssignPermissions,
            IdentityPermissions.UsersView, IdentityPermissions.RolesView, IdentityPermissions.PermissionsView
        ]);
        var target = await host.UserAsync(host.TenantA, [TaskManagementPermissions.View]);

        var makeAdmin = await host.SendAsync(roleManager, HttpMethod.Put, $"/api/identity/users/{target.Id}/roles", new { roleIds = new[] { host.AdminRoleA } });
        Assert.Equal(HttpStatusCode.Forbidden, makeAdmin.StatusCode);

        var emptyRole = await host.RoleAsync(host.TenantA);
        var widen = await host.SendAsync(roleManager, HttpMethod.Put, $"/api/identity/roles/{emptyRole}/permissions",
            new { permissionIds = await host.PermissionIdsAsync(IdentityPermissions.UsersDelete) });
        Assert.Equal(HttpStatusCode.Forbidden, widen.StatusCode);

        var stripAdmin = await host.SendAsync(host.Admin, HttpMethod.Put, $"/api/identity/roles/{host.AdminRoleA}/permissions", new { permissionIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Forbidden, stripAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(host.Admin, HttpMethod.Get, "/api/identity/users")).StatusCode);
    }

    // ---------------------------------------------------------------- organizations

    [Fact]
    public async Task AnotherOrganizationsAdministrator_CannotReachThisOrganization()
    {
        var userA = await host.UserAsync(host.TenantA, MemberPermissions);
        var taskA = await host.CreatedTaskIdAsync(userA, "Org A only");

        // Asking for organization A explicitly still returns B's own users.
        var users = await Json(await host.SendAsync(host.AdminB, HttpMethod.Get, $"/api/identity/users?tenantId={host.TenantA}&pageSize=200"));
        Assert.All(users.GetProperty("items").EnumerateArray(), user => Assert.Equal(host.TenantB, user.GetProperty("tenantId").GetGuid()));

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(host.AdminB, HttpMethod.Patch, $"/api/identity/users/{userA.Id}/status", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(host.AdminB, HttpMethod.Get, $"/api/identity/users/{userA.Id}/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(host.AdminB, HttpMethod.Put, $"/api/identity/users/{userA.Id}/roles", new { roleIds = Array.Empty<Guid>() })).StatusCode);

        var create = await host.SendAsync(host.AdminB, HttpMethod.Post, "/api/identity/users", new
        {
            tenantId = host.TenantA, username = "0077777777", firstName = "Planted", lastName = "User",
            phoneNumber = "09127777777", password = "Correct-Pass-1"
        });
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);

        var roles = await Json(await host.SendAsync(host.AdminB, HttpMethod.Get, $"/api/identity/roles?tenantId={host.TenantA}"));
        Assert.All(roles.EnumerateArray(), role => Assert.Equal(host.TenantB, role.GetProperty("tenantId").GetGuid()));

        var audit = await Json(await host.SendAsync(host.AdminB, HttpMethod.Get, $"/api/platform/audit-logs?tenantId={host.TenantA}&pageSize=100"));
        Assert.All(audit.GetProperty("items").EnumerateArray(), entry => Assert.Equal(host.TenantB, entry.GetProperty("tenantId").GetGuid()));

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(host.AdminB, HttpMethod.Get, $"/api/task-management/tasks/{taskA}")).StatusCode);
        Assert.True(await host.UserIsActiveAsync(userA.Id));
    }

    // ---------------------------------------------------------------- tickets

    [Fact]
    public async Task Tickets_AreVisibleToTheirPeopleAndManagers_OfTheSameOrganization()
    {
        var author = await host.UserAsync(host.TenantA, MemberPermissions);
        var colleague = await host.UserAsync(host.TenantA, MemberPermissions);
        var ticketManager = await host.UserAsync(host.TenantA, [TicketingPermissions.View, TicketingPermissions.Manage]);
        var otherOrgManager = await host.UserAsync(host.TenantB, [TicketingPermissions.View, TicketingPermissions.Manage]);

        var created = await host.SendAsync(author, HttpMethod.Post, "/api/tickets", new { title = "Printer", description = "Broken", priority = "Medium" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var ticketId = (await Json(created)).GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(author, HttpMethod.Get, $"/api/tickets/{ticketId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(ticketManager, HttpMethod.Get, $"/api/tickets/{ticketId}")).StatusCode);

        // Another user's ticket id, or another organization's: nothing to see or change.
        foreach (var outsider in new[] { colleague, otherOrgManager })
        {
            Assert.False((await host.SendAsync(outsider, HttpMethod.Get, $"/api/tickets/{ticketId}")).IsSuccessStatusCode);
            Assert.False((await host.SendAsync(outsider, HttpMethod.Post, $"/api/tickets/{ticketId}/comments", new { text = "Hi" })).IsSuccessStatusCode);
        }

        Assert.False((await host.SendAsync(otherOrgManager, HttpMethod.Put, $"/api/tickets/{ticketId}/status", new { status = "Closed" })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(colleague, HttpMethod.Put, $"/api/tickets/{ticketId}/priority", new { priority = "High" })).StatusCode);

        // A manager assigns only within the organization.
        Assert.False((await host.SendAsync(ticketManager, HttpMethod.Put, $"/api/tickets/{ticketId}/assign", new { userId = otherOrgManager.Id })).IsSuccessStatusCode);
        Assert.True((await host.SendAsync(ticketManager, HttpMethod.Put, $"/api/tickets/{ticketId}/assign", new { userId = colleague.Id })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(colleague, HttpMethod.Get, $"/api/tickets/{ticketId}")).StatusCode);
    }

    // ---------------------------------------------------------------- files

    [Fact]
    public async Task AFilesContent_IsOnlyForThoseWhoSeeItsTask()
    {
        var owner = await host.UserAsync(host.TenantA, MemberPermissions);
        var outsider = await host.UserAsync(host.TenantA, MemberPermissions);
        var taskId = await host.CreatedTaskIdAsync(owner, "With attachment");

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("secret")) { Headers = { ContentType = new MediaTypeHeaderValue("text/plain") } }, "file", "secret.txt");
        var upload = await host.SendAsync(owner, HttpMethod.Post, $"/api/task-management/files/tasks/{taskId}", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var file = await Json(upload);
        var fileId = file.GetProperty("fileId").GetGuid();
        var linkId = file.GetProperty("linkId").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(owner, HttpMethod.Get, $"/api/task-management/files/{fileId}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(outsider, HttpMethod.Get, $"/api/task-management/files/{fileId}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(outsider, HttpMethod.Delete, $"/api/task-management/files/{linkId}")).StatusCode);
        Assert.Empty((await Json(await host.SendAsync(outsider, HttpMethod.Get, $"/api/task-management/tasks/{taskId}/files"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(outsider, HttpMethod.Get, $"/api/task-management/tasks/{taskId}/activity")).StatusCode);
    }

    // ---------------------------------------------------------------- catalogue

    [Fact]
    public void EveryPermission_HasAPersianTitle()
    {
        using var scope = host.Services.CreateScope();
        var definitions = scope.ServiceProvider.GetServices<IPermissionCatalog>().SelectMany(catalog => catalog.GetPermissions()).ToList();

        Assert.NotEmpty(definitions);
        Assert.All(definitions, definition => Assert.Matches("[؀-ۿ]", definition.Description));
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.Name).Distinct().Count());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // ---------------------------------------------------------------- host

    public sealed record Caller(Guid Id, string Token);

    public sealed class Host : IAsyncLifetime
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;
        private int _serial;

        public Guid TenantA { get; } = Guid.NewGuid();
        public Guid TenantB { get; } = Guid.NewGuid();
        public Guid AdminRoleA { get; } = Guid.NewGuid();
        public Caller Admin { get; private set; } = null!;
        public Caller AdminB { get; private set; } = null!;
        public IServiceProvider Services => _app.Services;

        /// <summary>The SMS the platform handed to its provider ("test"; off until a tenant enables it).</summary>
        public RecordingSmsProvider Sms { get; } = new();

        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:UserGroups:Enabled"] = "true",
                // Like Rozet: this product manages identity, platform and task permissions only;
                // Ticketing's still exist and are enforced for whoever holds them.
                ["Identity:ManagedPermissionModules:0"] = "Identity",
                ["Identity:ManagedPermissionModules:1"] = "Platform",
                ["Identity:ManagedPermissionModules:2"] = "TaskManagement",
                // Like the task-management product: its SMS panel shows only its own templates.
                ["Notifications:SmsTemplates:Keys:0"] = "password_reset",
                ["Notifications:SmsTemplates:Keys:1"] = "task_assigned",
                ["Notifications:SmsTemplates:Keys:2"] = "recurring_task_reminder",
                ["Notifications:SmsTemplates:Keys:3"] = "task_due_changed",
                ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), $"access-tests-{Guid.NewGuid():N}"),
            });

            // The Rozet composition, minus the modules these tests do not reach.
            builder.Services.AddApplication();
            builder.Services.AddInfrastructure(builder.Configuration);
            builder.Services.AddTaskManagement();
            builder.Services.AddTaskManagementInfrastructure(builder.Configuration);
            // Task SMS through the platform's SMS panel, to a recording provider (never a real gateway).
            builder.Services.AddTaskNotificationsIntegration();
            builder.Services.AddSingleton<ISmsProvider>(Sms);
            builder.Services.AddTicketingApplication();
            builder.Services.AddTicketingInfrastructure(builder.Configuration);
            builder.Services.AddSignalR();

            // One in-memory store per context: each maps the shared identity tables with its own
            // model, which in-memory cannot share. Users and teams are mirrored into TaskManagement's
            // store below, standing in for the one SQL Server table both read.
            UseInMemory<NexusCoreDbContext>(builder.Services, Guid.NewGuid().ToString(), withInterceptors: true);
            UseInMemory<TaskManagementDbContext>(builder.Services, Guid.NewGuid().ToString(), withInterceptors: true);
            UseInMemory<TicketingDbContext>(builder.Services, Guid.NewGuid().ToString(), withInterceptors: false);

            var jwt = new JwtOptions();
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true, ValidateLifetime = true,
                        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    };
                });
            builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddAuthorization(options =>
            {
                foreach (var permission in IdentityPermissions.All)
                {
                    options.AddPolicy(permission.Name, policy =>
                        policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
                }
            });
            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

            _app = builder.Build();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.MapIdentityEndpoints();
            _app.MapUserGroupEndpoints();
            _app.MapTaskManagementEndpoints();
            _app.MapTicketEndpoints();
            await _app.StartAsync();
            _client = _app.GetTestClient();

            await SeedAsync();
        }

        private static void UseInMemory<TContext>(IServiceCollection services, string database, bool withInterceptors)
            where TContext : DbContext
        {
            services.RemoveAll<DbContextOptions<TContext>>();
            services.AddDbContext<TContext>((provider, options) =>
            {
                options.UseInMemoryDatabase(database);
                if (withInterceptors)
                {
                    options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>(), provider.GetRequiredService<DomainEventDispatchInterceptor>());
                }
            });
        }

        private async Task SeedAsync()
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            db.Tenants.AddRange(new Tenant(TenantA, "A", "a"), new Tenant(TenantB, "B", "b"));
            foreach (var definition in scope.ServiceProvider.GetServices<IPermissionCatalog>().SelectMany(catalog => catalog.GetPermissions()).DistinctBy(d => d.Name))
            {
                db.Permissions.Add(new Permission(Guid.NewGuid(), definition.Name, definition.Module, definition.Description));
            }

            await db.SaveChangesAsync();

            // Each organization has a built-in Administrator role with every permission except
            // the platform-wide one, like a tenant administrator.
            var all = await db.Permissions.Where(p => p.Name != IdentityPermissions.TenantsManageAll).Select(p => p.Name).ToArrayAsync();
            Admin = await UserAsync(TenantA, all, viaRole: true, roleId: AdminRoleA, systemRole: true);
            AdminB = await UserAsync(TenantB, all, viaRole: true, systemRole: true);
        }

        /// <summary>A new user with exactly these permissions (direct grants, or one role) and a signed token.</summary>
        public async Task<Caller> UserAsync(Guid tenantId, IEnumerable<string> permissions, bool viaRole = false, Guid? roleId = null, bool systemRole = false)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            var serial = Interlocked.Increment(ref _serial);
            var ids = await PermissionIdsAsync(permissions.ToArray());

            var user = new User(Guid.NewGuid(), tenantId, null, $"User {serial}", "not-used");
            user.UpdateContactDetails($"00{serial:D8}", null, notifySms: false);
            if (viaRole)
            {
                var role = new Role(roleId ?? Guid.NewGuid(), tenantId, systemRole ? "Administrator" : $"Role {serial}", isSystem: systemRole);
                role.SetPermissions(ids);
                db.Roles.Add(role);
                user.SetRoles([role.Id]);
            }
            else
            {
                user.SetDirectPermissions(ids);
            }

            db.Users.Add(user);
            await db.SaveChangesAsync();

            var taskDb = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            taskDb.Users.Add(new User(user.Id, tenantId, null, user.DisplayName, "not-used"));
            await taskDb.SaveChangesAsync();

            // Signed with no permissions: every request reads the user's current ones.
            var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().CreateAccessToken(user, []).Token;
            return new Caller(user.Id, token);
        }

        public async Task<Guid> RoleAsync(Guid tenantId)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            var role = new Role(Guid.NewGuid(), tenantId, $"Empty {Interlocked.Increment(ref _serial)}");
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            return role.Id;
        }

        public async Task<Guid> TeamAsync(Guid tenantId, params Guid[] members)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            var group = new UserGroup(Guid.NewGuid(), tenantId, $"Team {Interlocked.Increment(ref _serial)}");
            group.SetMembers(members);
            db.UserGroups.Add(group);
            await db.SaveChangesAsync();

            var taskDb = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            taskDb.UserGroups.Add(new UserGroup(group.Id, tenantId, group.Name));
            await taskDb.SaveChangesAsync();
            return group.Id;
        }

        public async Task<Guid[]> PermissionIdsAsync(params string[] names)
        {
            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
            return await db.Permissions.Where(p => names.Contains(p.Name)).Select(p => p.Id).ToArrayAsync();
        }

        public async Task<bool> UserIsActiveAsync(Guid userId)
        {
            using var scope = _app.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == userId)).IsActive;
        }

        public Task<HttpResponseMessage> CreateTaskAsync(Caller caller, string title, Guid? assignedUserId = null, Guid? assignedUserGroupId = null) =>
            SendAsync(caller, HttpMethod.Post, "/api/task-management/tasks", new
            {
                // Every new task has someone responsible; unless a test names one, the creator.
                title, dueDate = "2030-01-01", priority = "Medium", isProject = false, assignedUserId = assignedUserId ?? caller.Id, assignedUserGroupId
            });

        public async Task<Guid> CreatedTaskIdAsync(Caller caller, string title, Guid? assignedUserId = null, Guid? assignedUserGroupId = null)
        {
            var response = await CreateTaskAsync(caller, title, assignedUserId, assignedUserGroupId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Json(response)).GetProperty("id").GetGuid();
        }

        public Task<HttpResponseMessage> UpdateTaskAsync(Caller caller, Guid id, string title) =>
            SendAsync(caller, HttpMethod.Put, $"/api/task-management/tasks/{id}", new { title, dueDate = "2030-01-02", priority = "High" });

        public async Task<IReadOnlyList<Guid>> ListTaskIdsAsync(Caller caller)
        {
            var response = await SendAsync(caller, HttpMethod.Get, "/api/task-management/tasks?pageSize=200");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Json(response)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        }

        public async Task<HttpResponseMessage> SendAsync(Caller caller, HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", caller.Token);
            request.Content = body switch
            {
                null => null,
                HttpContent content => content,
                _ => JsonContent.Create(body)
            };
            return await _client.SendAsync(request);
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
