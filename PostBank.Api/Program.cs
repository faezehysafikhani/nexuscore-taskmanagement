using Chat.Api.Endpoints;
using Chat.Api.Hubs;
using Chat.Application;
using Chat.Infrastructure;
using Events.Api.Endpoints;
using Events.Application;
using Events.Infrastructure;
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
using Nexus.Integrations.ProjectWorkflow;
using Nexus.Integrations.ProjectWorkflow.Endpoints;
using Nexus.Integrations.StrategyAlignment;
using Nexus.Integrations.StrategyAlignment.Endpoints;
using Nexus.Integrations.StrategyAlignment.Infrastructure;
using Nexus.Knowledge;
using Nexus.Knowledge.Endpoints;
using Nexus.Knowledge.Infrastructure;
using Nexus.Organization;
using Nexus.Organization.Endpoints;
using Nexus.Organization.Infrastructure;
using Nexus.Portfolio;
using Nexus.Portfolio.Endpoints;
using Nexus.ProjectManagement.Agile;
using Nexus.ProjectManagement.Agile.Endpoints;
using Nexus.ProjectManagement.Agile.Infrastructure;
using Nexus.ProjectManagement.Core;
using Nexus.ProjectManagement.Core.Endpoints;
using Nexus.ProjectManagement.Core.Infrastructure;
using Nexus.ProjectManagement.Deliverables;
using Nexus.ProjectManagement.Deliverables.Endpoints;
using Nexus.ProjectManagement.Deliverables.Infrastructure;
using Nexus.ProjectManagement.Documents;
using Nexus.ProjectManagement.Documents.Endpoints;
using Nexus.ProjectManagement.Documents.Infrastructure;
using Nexus.ProjectManagement.Kpi;
using Nexus.ProjectManagement.Kpi.Endpoints;
using Nexus.ProjectManagement.Kpi.Infrastructure;
using Nexus.ProjectManagement.Progress;
using Nexus.ProjectManagement.Progress.Endpoints;
using Nexus.ProjectManagement.Progress.Infrastructure;
using Nexus.ProjectManagement.RiskManagement;
using Nexus.ProjectManagement.RiskManagement.Endpoints;
using Nexus.ProjectManagement.RiskManagement.Infrastructure;
using Nexus.ProjectManagement.StakeholderManagement;
using Nexus.ProjectManagement.StakeholderManagement.Endpoints;
using Nexus.ProjectManagement.StakeholderManagement.Infrastructure;
using Nexus.ProjectManagement.Team;
using Nexus.ProjectManagement.Team.Endpoints;
using Nexus.ProjectManagement.Team.Infrastructure;
using Nexus.ProjectManagement.Waterfall;
using Nexus.ProjectManagement.Waterfall.Endpoints;
using Nexus.ProjectManagement.Waterfall.Infrastructure;
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
using Serilog;
using System.Text;
using Ticketing.Api.Endpoints;
using Ticketing.Application;
using Ticketing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

// --- Application tier: NexusCore foundation, then every composed Nexus Module. Each call is
// self-contained - deleting any one of them (and its matching Infrastructure/Map* calls below)
// removes that capability entirely, with nothing left to clean up elsewhere. ---
builder.Services.AddApplication();
builder.Services.AddChatApplication();
builder.Services.AddEventsApplication();
builder.Services.AddTicketingApplication();
builder.Services.AddNotificationApplication();

builder.Services.AddOrganizationApplication();
builder.Services.AddCalendarApplication();
builder.Services.AddWorkflowApplication();
builder.Services.AddActionManagement();
builder.Services.AddKnowledgeManagement();
builder.Services.AddStrategyManagement();

builder.Services.AddProjectManagementCore();
builder.Services.AddWaterfallPlanning();
builder.Services.AddAgilePlanning();
builder.Services.AddProjectTeam();
builder.Services.AddProjectDeliverables();
builder.Services.AddProjectKpi();
builder.Services.AddRiskManagement();
builder.Services.AddStakeholderManagement();
builder.Services.AddProgressManagement();
builder.Services.AddProjectDocuments();

builder.Services.AddProjectWorkflowIntegration();
builder.Services.AddProjectStrategyAlignment();

builder.Services.AddPortfolio();
builder.Services.AddProjectReporting();

// --- Infrastructure tier: one DbContext per module, all pointed at the same DefaultConnection
// database (isolated by schema - see each module's own ToTable(name, schema) configuration). ---
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddChatInfrastructure(builder.Configuration);
builder.Services.AddEventsInfrastructure(builder.Configuration);
builder.Services.AddTicketingInfrastructure(builder.Configuration);
builder.Services.AddNotificationInfrastructure(builder.Configuration);

builder.Services.AddOrganizationInfrastructure(builder.Configuration);
builder.Services.AddCalendarInfrastructure(builder.Configuration);
builder.Services.AddWorkflowInfrastructure(builder.Configuration);
builder.Services.AddActionManagementInfrastructure(builder.Configuration);
builder.Services.AddKnowledgeManagementInfrastructure(builder.Configuration);
builder.Services.AddStrategyManagementInfrastructure(builder.Configuration);

builder.Services.AddProjectManagementCoreInfrastructure(builder.Configuration);
builder.Services.AddWaterfallPlanningInfrastructure(builder.Configuration);
builder.Services.AddAgilePlanningInfrastructure(builder.Configuration);
builder.Services.AddProjectTeamInfrastructure(builder.Configuration);
builder.Services.AddProjectDeliverablesInfrastructure(builder.Configuration);
builder.Services.AddProjectKpiInfrastructure(builder.Configuration);
builder.Services.AddRiskManagementInfrastructure(builder.Configuration);
builder.Services.AddStakeholderManagementInfrastructure(builder.Configuration);
builder.Services.AddProgressManagementInfrastructure(builder.Configuration);
builder.Services.AddProjectDocumentsInfrastructure(builder.Configuration);

builder.Services.AddProjectStrategyAlignmentInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PostBank API",
        Version = "v1",
        Description = "PostBank - a project-management product built on the Nexus Platform: NexusCore foundation plus the full project-management capability set, composed as independent modules."
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
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// Every module's own AddXxxApplication() call above already added its own permission policies
// via its own services.AddAuthorization(...) call (additive across modules - see e.g.
// Nexus.Portfolio/DependencyInjection.cs). This one covers NexusCore's own base identity
// permissions, the one set no module owns.
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in IdentityPermissions.All)
    {
        options.AddPolicy(permission.Name, policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
    }
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapChatEndpoints();
app.MapTicketEndpoints();
app.MapNotificationEndpoints();
app.MapEventEndpoints();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapIdentityEndpoints();
if (builder.Configuration.IsUserGroupFeatureEnabled())
{
    app.MapUserGroupEndpoints();
}

app.MapOrganizationEndpoints();
app.MapCalendarEndpoints();
app.MapWorkflowEndpoints();
app.MapActionEndpoints();
app.MapKnowledgeDocumentEndpoints();
app.MapStrategyEndpoints();

app.MapProjectEndpoints();
app.MapWaterfallEndpoints();
app.MapAgileTaskEndpoints();
app.MapTeamEndpoints();
app.MapDeliverableEndpoints();
app.MapKpiEndpoints();
app.MapRiskEndpoints();
app.MapStakeholderEndpoints();
app.MapProgressEndpoints();
app.MapProjectDocumentEndpoints();

app.MapProjectWorkflowEndpoints();
app.MapProjectStrategyAlignmentEndpoints();

app.MapPortfolioEndpoints();
app.MapDashboardEndpoints();

if (builder.Configuration.GetValue("Database:SeedOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var cancellationToken = CancellationToken.None;

    // Seeds NexusCoreDbContext's own schema (via ModuleSchemaInitializer internally) plus the
    // default tenant/admin and every permission contributed by an installed module's
    // IPermissionCatalog. Every other module's DbContext then needs its own schema created too -
    // see ModuleSchemaInitializer's own comment for why each needs its own call rather than one
    // shared EnsureCreatedAsync.
    await services.GetRequiredService<DefaultDataSeeder>().SeedAsync(cancellationToken);

    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<OrganizationDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<CalendarDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<WorkflowDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ActionsDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<KnowledgeDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<StrategyManagementDbContext>(), cancellationToken);

    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ProjectManagementCoreDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<WaterfallDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<AgileDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<TeamDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<DeliverablesDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<KpiDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<RiskManagementDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<StakeholderManagementDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ProgressDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ProjectDocumentsDbContext>(), cancellationToken);

    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<StrategyAlignmentDbContext>(), cancellationToken);
}

app.Run();
