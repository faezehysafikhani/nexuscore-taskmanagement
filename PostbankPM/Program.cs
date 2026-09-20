using Chat.Api.Endpoints;
using Chat.Api.Hubs;
using Chat.Application;
using Chat.Infrastructure;
using Chat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nexus.Actions;
using Nexus.Actions.Endpoints;
using Nexus.Actions.Infrastructure;
using Nexus.Calendar;
using Nexus.Calendar.Endpoints;
using Nexus.Calendar.Infrastructure;
using Nexus.Knowledge;
using Nexus.Knowledge.Endpoints;
using Nexus.Knowledge.Infrastructure;
using Nexus.Organization;
using Nexus.Organization.Endpoints;
using Nexus.Organization.Infrastructure;
using Nexus.ProjectManagement.Core;
using Nexus.ProjectManagement.Core.Endpoints;
using Nexus.ProjectManagement.Core.Infrastructure;
using Nexus.ProjectManagement.Progress;
using Nexus.ProjectManagement.Progress.Endpoints;
using Nexus.ProjectManagement.Progress.Infrastructure;
using Nexus.ProjectManagement.Team;
using Nexus.ProjectManagement.Team.Endpoints;
using Nexus.ProjectManagement.Team.Infrastructure;
using Nexus.Reporting;
using Nexus.Reporting.Endpoints;
using Nexus.StrategyManagement;
using Nexus.StrategyManagement.Endpoints;
using Nexus.StrategyManagement.Infrastructure;
using Nexus.Workflow;
using Nexus.Workflow.Endpoints;
using Nexus.Workflow.Infrastructure;
using NexusCore.Application;
using NexusCore.Application.Endpoints;
using NexusCore.Application.Identity.Permissions;
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
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

// Application services supplied by NexusCore and the reusable modules selected for this product.
builder.Services.AddApplication();
builder.Services.AddChatApplication();
builder.Services.AddNotificationApplication();
builder.Services.AddOrganizationApplication();
builder.Services.AddCalendarApplication();
builder.Services.AddWorkflowApplication();
builder.Services.AddActionManagement();
builder.Services.AddKnowledgeManagement();
builder.Services.AddStrategyManagement();
builder.Services.AddProjectManagementCore();
builder.Services.AddProjectTeam();
builder.Services.AddProgressManagement();
builder.Services.AddProjectReporting();

// Persistence remains owned by the reusable modules; this host only supplies configuration.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddChatInfrastructure(builder.Configuration);
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddOrganizationInfrastructure(builder.Configuration);
builder.Services.AddCalendarInfrastructure(builder.Configuration);
builder.Services.AddWorkflowInfrastructure(builder.Configuration);
builder.Services.AddActionManagementInfrastructure(builder.Configuration);
builder.Services.AddKnowledgeManagementInfrastructure(builder.Configuration);
builder.Services.AddStrategyManagementInfrastructure(builder.Configuration);
builder.Services.AddProjectManagementCoreInfrastructure(builder.Configuration);
builder.Services.AddProjectTeamInfrastructure(builder.Configuration);
builder.Services.AddProgressManagementInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PostbankPM API",
        Version = "v1",
        Description = "Postbank project-management API composed from NexusCore modules."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
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
});

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
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
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
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

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("PostbankPM.UI", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("PostbankPM.UI");
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapIdentityEndpoints();
if (builder.Configuration.IsUserGroupFeatureEnabled())
{
    app.MapUserGroupEndpoints();
}

app.MapChatEndpoints();
app.MapNotificationEndpoints();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapOrganizationEndpoints();
app.MapCalendarEndpoints();
app.MapWorkflowEndpoints();
app.MapActionEndpoints();
app.MapKnowledgeDocumentEndpoints();
app.MapStrategyEndpoints();
app.MapProjectEndpoints();
app.MapTeamEndpoints();
app.MapProgressEndpoints();
app.MapDashboardEndpoints();

if (builder.Configuration.GetValue("Database:SeedOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var cancellationToken = CancellationToken.None;

    // Every module owns a DbContext. Creating NexusCore's tables does not create the tables
    // belonging to the other contexts, even when they share the same physical database.
    await services.GetRequiredService<DefaultDataSeeder>().SeedAsync(cancellationToken);

    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<OrganizationDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<CalendarDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<WorkflowDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ActionsDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<KnowledgeDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<StrategyManagementDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ProjectManagementCoreDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<TeamDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ProgressDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<NotificationDbContext>(), cancellationToken);

    // Chat intentionally uses its own connection string/database.
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ChatDbContext>(), cancellationToken);
}

app.Run();
