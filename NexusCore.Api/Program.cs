using NexusCore.Application.Common;
using Chat.Api.Endpoints;
using Chat.Api.Hubs;
using Chat.Application;
using Chat.Infrastructure;
using Chat.Infrastructure.Persistence;
using Events.Api.Endpoints;
using Events.Application;
using Events.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NexusCore.Application;
using NexusCore.Application.Endpoints;
using NexusCore.Application.Identity.Permissions;
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
using Serilog;
using System.Reflection;
using System.Text;
using Ticketing.Api.Endpoints;
using Ticketing.Application;
using Ticketing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

builder.Services.AddApplication();
builder.Services.AddChatApplication();
builder.Services.AddEventsApplication();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddChatInfrastructure(builder.Configuration);
builder.Services.AddEventsInfrastructure(builder.Configuration);
builder.Services.AddTicketingApplication();

builder.Services.AddTicketingInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddNotificationApplication();
builder.Services.AddNotificationInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddNexusCoreRateLimiting(builder.Configuration);

builder.Services.AddSignalR();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NexusCore API",
        Version = "v1",
        Description = "Modular monolith core platform API for identity, tenancy, permissions, audit, and settings."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter a valid JWT bearer token."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (builder.Environment.IsProduction())
{
    jwtOptions.EnsureSafeForProduction();
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in IdentityPermissions.All)
    {
        options.AddPolicy(permission.Name, policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
    }
});
// Allowed origins live in configuration (Cors:AllowedOrigins) rather than hardcoded here, so a
// deployment can point the frontend at a different host/IP without a code change and rebuild.
var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        if (corsAllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsAllowedOrigins).AllowAnyMethod().AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// Unexpected exceptions: logged here, answered with a Persian message and no internals.
app.UseSafeErrorResponses();

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
app.MapChatEndpoints();
app.MapTicketEndpoints();
app.MapNotificationEndpoints();
app.MapEventEndpoints();
app.MapHub<ChatHub>("/hubs/chat")
    .RequireRateLimiting(NexusRateLimitPolicies.Realtime);
app.MapHub<NotificationHub>("/hubs/notifications")
    .RequireRateLimiting(NexusRateLimitPolicies.Realtime);

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapIdentityEndpoints();
if (builder.Configuration.IsUserGroupFeatureEnabled())
{
    app.MapUserGroupEndpoints();
}

if (builder.Configuration.GetValue("Database:SeedOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var cancellationToken = CancellationToken.None;

    await services.GetRequiredService<DefaultDataSeeder>().SeedAsync(cancellationToken);

    // In-app notifications' schema. Chat's own schema init/patch runs unconditionally below,
    // regardless of this flag - see the comment there for why.
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<NotificationDbContext>(), cancellationToken);
}

// Chat's schema must stay current even when Database:SeedOnStartup is disabled - the setting
// docs/Core-Packaging-Government-Deployment.md tells production deployments to keep disabled
// after their initial setup. EnsureCreatedAsync/EnsureColumnAsync only ever create a missing
// table or add a missing column; they never seed or change data, so running them on every
// restart is safe regardless of that flag. Without this, a deployment that disables seeding
// (exactly as documented) would never receive a later additive Chat schema change - such as
// team conversations' TeamId column - and chat would keep failing with a 500 no matter how many
// times the fix for that change is deployed.
{
    using var chatSchemaScope = app.Services.CreateScope();
    var chatDb = chatSchemaScope.ServiceProvider.GetRequiredService<ChatDbContext>();
    var chatCancellationToken = CancellationToken.None;
    await ModuleSchemaInitializer.EnsureCreatedAsync(chatDb, chatCancellationToken);

    // Columns added to Chat's model after a deployment's Conversations table already existed
    // (team conversations): EnsureCreatedAsync above cannot add these to an existing table on
    // its own, so a live database that predates them would otherwise 500 on any query that
    // touches Conversations. Safe to call on every restart.
    await ModuleSchemaInitializer.EnsureColumnAsync(
        chatDb, "dbo.Conversations", "TeamId",
        "ALTER TABLE dbo.Conversations ADD TeamId uniqueidentifier NULL;",
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversations_TeamId' AND object_id = OBJECT_ID(N'dbo.Conversations')) " +
        "CREATE UNIQUE INDEX IX_Conversations_TeamId ON dbo.Conversations(TeamId) WHERE TeamId IS NOT NULL;",
        chatCancellationToken);
}

app.Run();
