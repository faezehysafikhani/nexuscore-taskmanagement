using Chat.Api.Endpoints;
using Chat.Api.Hubs;
using Chat.Application;
using Chat.Infrastructure;
using Chat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nexus.Integrations.TaskNotifications;
using Nexus.TaskManagement;
using Nexus.TaskManagement.Endpoints;
using Nexus.TaskManagement.Infrastructure;
using NexusCore.Application;
using NexusCore.Application.Common;
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

// TMPB intentionally composes only the shared platform, chat, notifications and task manager.
builder.Services.AddApplication();
builder.Services.AddChatApplication();
builder.Services.AddNotificationApplication();
builder.Services.AddTaskManagement();
builder.Services.AddTaskNotificationsIntegration();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddChatInfrastructure(builder.Configuration);
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddTaskManagementInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TMPB API",
        Version = "v1",
        Description = "TMPB host composed from packaged NexusCore, Chat, Notifications and TaskManagement modules."
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
                Reference = new OpenApiReference { Type = SecuritySchemeType.Http, Id = "Bearer" }
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

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in IdentityPermissions.All)
    {
        options.AddPolicy(permission.Name, policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
    }
});

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
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapIdentityEndpoints();
if (builder.Configuration.IsUserGroupFeatureEnabled())
{
    app.MapUserGroupEndpoints();
}

app.MapChatEndpoints();
app.MapNotificationEndpoints();
app.MapTaskManagementEndpoints();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<NotificationHub>("/hubs/notifications");

if (builder.Configuration.GetValue("Database:SeedOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var cancellationToken = CancellationToken.None;

    await services.GetRequiredService<DefaultDataSeeder>().SeedAsync(cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<NotificationDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<ChatDbContext>(), cancellationToken);
    await ModuleSchemaInitializer.EnsureCreatedAsync(services.GetRequiredService<TaskManagementDbContext>(), cancellationToken);
}

app.Run();
