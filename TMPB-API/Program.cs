using System.Text;

using NexusCore.Application.Common;
using NexusCore.Application;
using NexusCore.Application.Endpoints;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Security;
using NexusCore.Application.Security.RateLimiting;

using NexusCore.Infrastructure;
using NexusCore.Infrastructure.Identity;
using NexusCore.Infrastructure.Persistence;
using NexusCore.Infrastructure.Security;

using Chat.Api.Endpoints;
using Chat.Api.Hubs;
using Chat.Application;
using Chat.Infrastructure;
using Chat.Infrastructure.Persistence;

using Notifications.Api.Endpoints;
using Notifications.Api.Hubs;
using Notifications.Application;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Persistence;

using Nexus.TaskManagement;
using Nexus.TaskManagement.Endpoints;
using Nexus.TaskManagement.Infrastructure;

using Nexus.Integrations.TaskNotifications;
using Nexus.Integrations.TaskReporting;

using Nexus.Calendar;
using Nexus.Calendar.Infrastructure;
using Nexus.Calendar.Infrastructure.Meetings;
using Nexus.Integrations.CalendarTasks;
using Nexus.Integrations.CalendarOrgStructure;
using Nexus.Organization;
using Nexus.Organization.Endpoints;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure;
using Nexus.OrgStructure.Endpoints;
using Nexus.OrgStructure.Infrastructure;


using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using Serilog;

using TMPB_API;


var builder = WebApplication.CreateBuilder(args);


// ======================================================
// Logging
// ======================================================

builder.Host.UseSerilog((context, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console());


// ======================================================
// Application Modules
// ======================================================

// Shared Core / Identity
builder.Services.AddApplication();

// Chat
builder.Services.AddChatApplication();

// Notifications
builder.Services.AddNotificationApplication();

// Task Management
builder.Services.AddTaskManagement();

// Integration between TaskManagement and Notifications
builder.Services.AddTaskNotificationsIntegration();

// Organization units (deputies / departments)
builder.Services.AddOrganizationApplication();

// Organization structure: companies, positions, manager / subordinates
builder.Services.AddOrgStructure();

// Calendar and meetings (the meeting people follow the organization structure; minutes make tasks)
builder.Services.AddCalendarApplication();

// Task reports along the organization structure
builder.Services.AddTaskReporting();
builder.Services.AddCalendarMeetings(builder.Configuration);
builder.Services.AddCalendarOrgStructureIntegration();
builder.Services.AddCalendarTasksIntegration();


// ======================================================
// Infrastructure Modules
// ======================================================

// Shared Core / Identity
builder.Services.AddInfrastructure(builder.Configuration);

// Chat
builder.Services.AddChatInfrastructure(builder.Configuration);

// Notifications
builder.Services.AddNotificationInfrastructure(builder.Configuration);

// Task Management
builder.Services.AddTaskManagementInfrastructure(
    builder.Configuration);

// Organization units
builder.Services.AddOrganizationInfrastructure(
    builder.Configuration);

// Organization structure
builder.Services.AddOrgStructureInfrastructure(
    builder.Configuration);

// Calendar and meetings
builder.Services.AddCalendarInfrastructure(builder.Configuration);


// ======================================================
// Authorization
// ======================================================

builder.Services.AddSingleton<
    IAuthorizationHandler,
    PermissionAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in IdentityPermissions.All)
    {
        options.AddPolicy(
            permission.Name,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new PermissionRequirement(permission.Name)));
    }
});


// ======================================================
// SignalR
// ======================================================

builder.Services.AddSignalR();


// ======================================================
// JSON Configuration
// ======================================================

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});


// ======================================================
// API Explorer + Swagger
// ======================================================

builder.Services.AddEndpointsApiExplorer();


// ======================================================
// Security: rate limits, client address behind a proxy, request size limits, key storage
// ======================================================

// Per-user / per-client request limits (NexusCore:RateLimiting); sign-in is limited per client address.
builder.Services.AddNexusCoreRateLimiting(builder.Configuration);

// Behind a reverse proxy, the real client address is believed only from
// NexusCore:ForwardedHeaders:KnownProxies / KnownNetworks (empty: loopback only).
builder.Services.AddNexusCoreForwardedHeaders(builder.Configuration);

builder.Services.AddNexusCoreRequestLimits();

// The keys that encrypt the SMS panel's API key and the LDAP password. With
// DataProtection:KeysPath set they are kept in that folder, so they survive restarts and app
// pool changes (the folder must be writable by the app pool and backed up with the database).
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("TMPB-API")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TMPB API",
        Version = "v1",
        Description = "TMPB - Built on NexusCore modular platform."
    });

    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter a valid JWT bearer token."
        });

    options.AddSecurityRequirement(
        new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                []
            }
        });
});


// ======================================================
// JWT Authentication
// ======================================================

var jwtOptions =
    builder.Configuration
        .GetSection(JwtOptions.SectionName)
        .Get<JwtOptions>()
    ?? new JwtOptions();

if (!builder.Environment.IsDevelopment())
{
    // Placeholder values shipped as defaults/examples in source - never acceptable as the real
    // production signing key. A production host must not start with a missing, placeholder, or
    // too-short key - that would let anyone forge a valid access token.
    var insecureJwtSigningKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "",
        "change-me-to-a-strong-production-secret-at-least-32-characters",
        "replace-this-development-secret-with-a-production-secret-32chars",
    };

    if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey)
        || insecureJwtSigningKeys.Contains(jwtOptions.SigningKey)
        || jwtOptions.SigningKey.Length < 32)
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey is missing, a development placeholder, or shorter than 32 characters. " +
            "Set a real secret via the Jwt__SigningKey environment variable (or another production " +
            "configuration source) before starting this service in a production environment.");
    }
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,

                ValidateAudience = true,

                ValidateIssuerSigningKey = true,

                ValidateLifetime = true,

                ValidIssuer = jwtOptions.Issuer,

                ValidAudience = jwtOptions.Audience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            jwtOptions.SigningKey)),

                ClockSkew = TimeSpan.FromMinutes(1)
            };

        // SignalR authentication
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken =
                    context.Request.Query["access_token"];

                var path =
                    context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken)
                    && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });


// ======================================================
// CORS
// ======================================================

var corsAllowedOrigins =
    builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>()
    ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        if (corsAllowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(corsAllowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
    });
});


// ======================================================
// Build Application
// ======================================================

var app = builder.Build();


// ======================================================
// Middleware
// ======================================================

// Behind a reverse proxy: the real client address, believed only from the configured proxies.
app.UseNexusCoreForwardedHeaders();

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

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("FrontendPolicy");

app.UseSerilogRequestLogging();

app.UseAuthentication();

app.UseNexusCoreRateLimiting();

app.UseAuthorization();


// ======================================================
// Root Endpoint
// ======================================================

app.MapGet("/", () =>
    Results.Redirect("/swagger"))
    .ExcludeFromDescription();


// ======================================================
// NexusCore / Identity Endpoints
// ======================================================

app.MapIdentityEndpoints();

// Browser CSP violation reports (rate limited).
app.MapSecurityReportEndpoints();
Console.WriteLine(
    $"UserGroups Enabled: {builder.Configuration.IsUserGroupFeatureEnabled()}"
);
if (builder.Configuration.IsUserGroupFeatureEnabled())
{
    app.MapUserGroupEndpoints();
}


// ======================================================
// Task Management Endpoints
// ======================================================

app.MapTaskManagementEndpoints();
app.MapMeetingEndpoints();


// ======================================================
// Notification Endpoints
// ======================================================

app.MapNotificationEndpoints();


// ======================================================
// Chat Endpoints
// ======================================================

app.MapChatEndpoints();


// ======================================================
// SignalR Hubs
// ======================================================

app.MapHub<ChatHub>("/hubs/chat")
    .RequireRateLimiting(NexusRateLimitPolicies.Realtime);

app.MapHub<NotificationHub>("/hubs/notifications")
    .RequireRateLimiting(NexusRateLimitPolicies.Realtime);


// ======================================================
// Organization Endpoints
// ======================================================

app.MapOrganizationEndpoints();

app.MapOrgStructureEndpoints();

app.MapTaskReportEndpoints();


// ======================================================
// Database Initialization
// ======================================================

if (builder.Configuration.GetValue(
    "Database:SeedOnStartup",
    true))
{
    using var scope = app.Services.CreateScope();

    var services = scope.ServiceProvider;

    var cancellationToken =
        CancellationToken.None;


    // Core Identity, initial tenant and administrator
    await services
        .GetRequiredService<DefaultDataSeeder>()
        .SeedAsync(cancellationToken);


    // TaskManagement schema
    await ModuleSchemaInitializer.EnsureCreatedAsync(
        services.GetRequiredService<TaskManagementDbContext>(),
        cancellationToken);


    // Notifications schema
    await ModuleSchemaInitializer.EnsureCreatedAsync(
        services.GetRequiredService<NotificationDbContext>(),
        cancellationToken);


    // Chat schema
    await ModuleSchemaInitializer.EnsureCreatedAsync(
        services.GetRequiredService<ChatDbContext>(),
        cancellationToken);
}


// Organization and OrgStructure schemas (EF Core migrations): created on a new database, brought
// up to date on an existing one - see OrgStructureSchema. Off: create them with the scripts in
// db/ instead (see DEPLOYMENT-CONFIG.md).
if (builder.Configuration.GetValue(
    "Database:InitializeOrgStructureSchema",
    true))
{
    using var scope = app.Services.CreateScope();

    var services = scope.ServiceProvider;

    await OrgStructureSchema.EnsureAsync(
        services.GetRequiredService<OrganizationDbContext>(),
        "organization.OrganizationUnits",
        CancellationToken.None);

    await OrgStructureSchema.EnsureAsync(
        services.GetRequiredService<OrgStructureDbContext>(),
        "org_structure.Companies",
        CancellationToken.None);
}


// ======================================================
// Run
// ======================================================

// Meeting schema (calendar.Meetings, calendar.MeetingParticipants): created on a new database; off: run db/upgrade-18-calendar-meetings.sql instead.
if (builder.Configuration.GetValue("Database:InitializeMeetingSchema", true))
{
    using var scope = app.Services.CreateScope();
    await ModuleSchemaInitializer.EnsureCreatedAsync(scope.ServiceProvider.GetRequiredService<MeetingDbContext>(), CancellationToken.None);
}

app.Run();
