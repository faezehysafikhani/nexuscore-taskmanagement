using System.Text;
using Chat.Api.Endpoints;
using Chat.Api.Hubs;
using Chat.Application;
using Chat.Infrastructure;
using Chat.Infrastructure.Persistence;
using Events.Api.Endpoints;
using Events.Application;
using Events.Infrastructure;
using Events.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Nexus.Calendar;
using Nexus.Calendar.Endpoints;
using Nexus.Calendar.Infrastructure;
using Nexus.Calendar.IranianHolidays;
using Nexus.Organization;
using Nexus.Organization.Endpoints;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Domain;
using Nexus.OrgStructure.Endpoints;
using Nexus.OrgStructure.Infrastructure;
using Nexus.Workflow;
using Nexus.Workflow.Endpoints;
using Nexus.Workflow.Infrastructure;
using NexusCore.Application;
using NexusCore.Application.Common;
using NexusCore.Application.Endpoints;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Security;
using NexusCore.Application.Security.RateLimiting;
using NexusCore.Infrastructure;
using NexusCore.Infrastructure.Identity;
using NexusCore.Infrastructure.Persistence;
using NexusCore.Infrastructure.Security;
using Notifications.Api.Endpoints;
using Notifications.Api.Hubs;
using Notifications.Application;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Persistence;
using Ticketing.Api.Endpoints;
using Ticketing.Application;
using Ticketing.Infrastructure;

namespace Nexus.Bundles;

/// <summary>Choices a host may make about the platform bundle. Defaults suit a single-tenant product.</summary>
public sealed class NexusPlatformOptions
{
    /// <summary>Only one default calendar per tenant.</summary>
    public bool CalendarSingleDefaultPerTenant { get; set; } = true;

    /// <summary>A workflow step must name its approver.</summary>
    public bool WorkflowRequireDesignatedApprover { get; set; } = true;

    /// <summary>Registers Iran's official holidays for the calendar.</summary>
    public bool IranianHolidays { get; set; } = true;

    /// <summary>
    /// The organization structure (nested units, node types, companies, positions). The units screen of the UI creates a
    /// unit placed under another one through it (<c>/api/org-structure/nodes</c>), so a product with that screen needs it.
    /// </summary>
    public bool OrgStructure { get; set; } = true;

    /// <summary>Chat, ticketing, events and notifications (with their SignalR hubs). Turn off for a host that only needs identity and organization.</summary>
    public bool Collaboration { get; set; } = true;
}

/// <summary>
/// The NexusCore platform in four calls: <c>AddNexusPlatform</c> (services, JWT, policies, CORS, rate limits),
/// <c>UseNexusPlatform</c> (the middleware pipeline in the right order), <c>MapNexusPlatform</c> (endpoints and hubs)
/// and <c>InitializeNexusPlatformAsync</c> (seed and schema).
/// </summary>
public static class NexusPlatformBundle
{
    public const string CorsPolicyName = "FrontendPolicy";

    public static IServiceCollection AddNexusPlatform(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        Action<NexusPlatformOptions>? configure = null)
    {
        var options = new NexusPlatformOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // Application tier
        services.AddApplication();
        services.AddOrganizationApplication();
        if (options.OrgStructure)
        {
            services.AddOrgStructure();
        }
        services.AddCalendarApplication(o => o.SingleDefaultPerTenant = options.CalendarSingleDefaultPerTenant);
        if (options.IranianHolidays)
        {
            services.AddIranianOfficialHolidays();
        }
        services.AddWorkflowApplication(o => o.RequireDesignatedApprover = options.WorkflowRequireDesignatedApprover);
        if (options.Collaboration)
        {
            services.AddChatApplication();
            services.AddEventsApplication();
            services.AddTicketingApplication();
            services.AddNotificationApplication();
        }

        // Infrastructure tier: one DbContext per module, same database, isolated by schema.
        services.AddInfrastructure(configuration);
        services.AddOrganizationInfrastructure(configuration);
        if (options.OrgStructure)
        {
            services.AddOrgStructureInfrastructure(configuration);
        }
        services.AddCalendarInfrastructure(configuration);
        services.AddWorkflowInfrastructure(configuration);
        if (options.Collaboration)
        {
            services.AddChatInfrastructure(configuration);
            services.AddEventsInfrastructure(configuration);
            services.AddTicketingInfrastructure(configuration);
            services.AddNotificationInfrastructure(configuration);
            services.AddSignalR();
        }

        // Security shared by every NexusCore host.
        services.AddNexusCoreRateLimiting(configuration);
        services.AddNexusCoreForwardedHeaders(configuration);
        services.AddNexusCoreRequestLimits();

        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization(o =>
        {
            foreach (var permission in IdentityPermissions.All)
            {
                o.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        AddJwt(services, configuration, environment);
        AddCors(services, configuration);
        return services;
    }

    private static void AddJwt(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (!environment.IsDevelopment())
        {
            // Refuses the development placeholder key and weak settings anywhere but Development.
            jwt.EnsureSafeForProduction();
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });
    }

    private static void AddCors(IServiceCollection services, IConfiguration configuration)
    {
        // Allowed origins live in configuration (Cors:AllowedOrigins); with none configured no cross-origin call is allowed.
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(o => o.AddPolicy(CorsPolicyName, policy =>
        {
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
            }
        }));
    }

    /// <summary>
    /// The shared pipeline, in the order the platform needs. Call it after any host-specific early middleware
    /// (for example Swagger in Development) and before <c>MapNexusPlatform</c>. Request logging is left to the host.
    /// </summary>
    public static WebApplication UseNexusPlatform(this WebApplication app)
    {
        // Behind a reverse proxy: the real client address, believed only from the configured proxies.
        app.UseNexusCoreForwardedHeaders();

        // Unexpected exceptions: logged, answered with a Persian message and no internals.
        app.UseSafeErrorResponses();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        // Tokens, sessions and one-time codes must not be kept by browsers or proxies.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/identity"))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["Cache-Control"] = "no-store";
                    context.Response.Headers["Pragma"] = "no-cache";
                    return Task.CompletedTask;
                });
            }

            await next();
        });

        app.UseHttpsRedirection();
        app.UseCors(CorsPolicyName);
        app.UseAuthentication();
        app.UseNexusCoreRateLimiting();
        app.UseAuthorization();
        return app;
    }

    public static WebApplication MapNexusPlatform(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<NexusPlatformOptions>();

        app.MapIdentityEndpoints();
        app.MapSecurityReportEndpoints();
        if (app.Configuration.IsUserGroupFeatureEnabled())
        {
            app.MapUserGroupEndpoints();
        }

        app.MapOrganizationEndpoints();
        if (options.OrgStructure)
        {
            app.MapOrgStructureEndpoints();
        }
        app.MapCalendarEndpoints();
        app.MapWorkflowEndpoints();

        if (options.Collaboration)
        {
            app.MapChatEndpoints();
            app.MapTicketEndpoints();
            app.MapNotificationEndpoints();
            app.MapEventEndpoints();
            app.MapHub<ChatHub>("/hubs/chat", o => o.CloseOnAuthenticationExpiration = true)
                .RequireRateLimiting(NexusRateLimitPolicies.Realtime);
            app.MapHub<NotificationHub>("/hubs/notifications", o => o.CloseOnAuthenticationExpiration = true)
                .RequireRateLimiting(NexusRateLimitPolicies.Realtime);
        }

        return app;
    }

    /// <summary>
    /// Seeds the default tenant, administrator and every permission the installed modules contribute, and creates the
    /// platform modules' schemas, when <c>Database:SeedOnStartup</c> is on. Chat's additive schema patch always runs
    /// (it only adds a missing table or column), so a deployment with seeding off still receives it.
    /// Run it after every bundle has been added and before <c>InitializeNexusProjectManagementAsync</c>.
    /// </summary>
    public static async Task InitializeNexusPlatformAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        var options = app.Services.GetRequiredService<NexusPlatformOptions>();

        if (app.Configuration.GetValue("Database:SeedOnStartup", true))
        {
            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;

            await sp.GetRequiredService<DefaultDataSeeder>().SeedAsync(cancellationToken);
            await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<OrganizationDbContext>(), cancellationToken);
            if (options.OrgStructure)
            {
                await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<OrgStructureDbContext>(), cancellationToken);
                await EnsureDefaultUnitTypeAsync(sp, cancellationToken);
            }
            await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<CalendarDbContext>(), cancellationToken);
            await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<WorkflowDbContext>(), cancellationToken);
            if (options.Collaboration)
            {
                await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<NotificationDbContext>(), cancellationToken);
                // Personal events (the management calendar). No host created its schema before: it came from a script.
                await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<EventsDbContext>(), cancellationToken);
            }

            // EnsureCreated never adds a column or table to a schema that already exists: bring an older database up to date.
            await OrganizationSchemaUpgrade.EnsureCurrentAsync(sp.GetRequiredService<OrganizationDbContext>(), cancellationToken);
            await CalendarSchemaUpgrade.EnsureCurrentAsync(sp.GetRequiredService<CalendarDbContext>(), cancellationToken);
            await WorkflowSchemaUpgrade.EnsureCurrentAsync(sp.GetRequiredService<WorkflowDbContext>(), cancellationToken);
            await WorkflowApproverPlaceholdersSchemaUpgrade.EnsureCurrentAsync(sp.GetRequiredService<WorkflowDbContext>(), cancellationToken);
        }

        if (options.OrgStructure)
        {
            // Additive tables a later Core release added: without them every sign-in fails, so this runs with seeding off too.
            using var upgradeScope = app.Services.CreateScope();
            await OrgStructureSchemaUpgrade.EnsureCurrentAsync(upgradeScope.ServiceProvider.GetRequiredService<OrgStructureDbContext>(), cancellationToken);
        }

        if (options.Collaboration)
        {
            using var chatScope = app.Services.CreateScope();
            var chatDb = chatScope.ServiceProvider.GetRequiredService<ChatDbContext>();
            await ModuleSchemaInitializer.EnsureCreatedAsync(chatDb, cancellationToken);

            // Column added after some deployments already had the Conversations table (team conversations).
            await ModuleSchemaInitializer.EnsureColumnAsync(
                chatDb, "dbo.Conversations", "TeamId",
                "ALTER TABLE dbo.Conversations ADD TeamId uniqueidentifier NULL;",
                "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversations_TeamId' AND object_id = OBJECT_ID(N'dbo.Conversations')) " +
                "CREATE UNIQUE INDEX IX_Conversations_TeamId ON dbo.Conversations(TeamId) WHERE TeamId IS NOT NULL;",
                cancellationToken);
        }
    }

    /// <summary>
    /// A schema made by EnsureCreated has no node types (only the upgrade scripts add them), and with no rule for
    /// «what may sit under what» the structure is flat: every unit placed under another is refused. So each organization
    /// gets the plain «unit» type and the rules "a unit may be at the top" and "a unit may sit under a unit", only when
    /// it has no types and no rules of its own. An organization that already shaped its structure is left alone.
    /// </summary>
    private static async Task EnsureDefaultUnitTypeAsync(IServiceProvider sp, CancellationToken cancellationToken)
    {
        var repository = sp.GetRequiredService<IOrgStructureRepository>();
        var unitOfWork = sp.GetRequiredService<IOrgStructureUnitOfWork>();
        var tenantIds = await sp.GetRequiredService<NexusCoreDbContext>().Tenants
            .AsNoTracking().Select(tenant => tenant.Id).ToListAsync(cancellationToken);

        foreach (var tenantId in tenantIds)
        {
            if ((await repository.ListNodeTypesAsync(tenantId, cancellationToken)).Count > 0
                || (await repository.ListNodeTypeRulesAsync(tenantId, cancellationToken)).Count > 0)
            {
                continue;
            }

            var unit = new NodeType(Guid.NewGuid(), tenantId, OrgNodeService.PlainUnitCode, "واحد", isDomainBoundary: false, sortOrder: 0);
            await repository.AddNodeTypeAsync(unit, cancellationToken);
            await repository.AddNodeTypeRuleAsync(new NodeTypeRule(Guid.NewGuid(), tenantId, null, unit.Id), cancellationToken);
            await repository.AddNodeTypeRuleAsync(new NodeTypeRule(Guid.NewGuid(), tenantId, unit.Id, unit.Id), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
