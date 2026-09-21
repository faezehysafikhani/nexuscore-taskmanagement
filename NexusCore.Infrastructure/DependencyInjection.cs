using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusCore.Application.Approvals;
using NexusCore.Application.Common;
using NexusCore.Application.Files;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Options;
using NexusCore.Infrastructure.Security;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Messaging;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Security;
using NexusCore.Infrastructure.Approvals;
using NexusCore.Infrastructure.Files;
using NexusCore.Infrastructure.Identity;
using NexusCore.Infrastructure.Messaging;
using NexusCore.Infrastructure.Persistence;
using NexusCore.Infrastructure.Persistence.Repositories;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(options =>
        {
            options.Issuer = configuration["Jwt:Issuer"] ?? options.Issuer;
            options.Audience = configuration["Jwt:Audience"] ?? options.Audience;
            options.SigningKey = configuration["Jwt:SigningKey"] ?? options.SigningKey;
            options.AccessTokenMinutes = int.TryParse(configuration["Jwt:AccessTokenMinutes"], out var minutes) ? minutes : options.AccessTokenMinutes;
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();

        services.AddDbContext<NexusCoreDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(
                    provider.GetRequiredService<AuditingInterceptor>(),
                    provider.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<NexusCoreDbContext>());
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IPlatformRepository, PlatformRepository>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<DefaultDataSeeder>();
        services.AddUserGroupFeature(configuration);

        // An endpoint that names an unregistered policy gets the standard permission policy (403
        // for users without it) instead of an InvalidOperationException (500).
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IApprovalRequester, NullApprovalRequester>();
        services.AddScoped<IFileStorage, LocalDiskFileStorage>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        services.Configure<SelfRegistrationOptions>(configuration.GetSection(SelfRegistrationOptions.SectionName));
        services.Configure<IdentitySeedOptions>(configuration.GetSection(IdentitySeedOptions.SectionName));
        services.Configure<PasswordResetOptions>(configuration.GetSection(PasswordResetOptions.SectionName));
        services.AddScoped<IPasswordResetLinkSender, EmailPasswordResetLinkSender>();
        services.Configure<SmtpEmailOptions>(configuration.GetSection(SmtpEmailOptions.SectionName));

        // Outgoing messages. Request logging is removed from these clients: the SMS API key and
        // the Telegram bot token are part of the request URL and must not reach the logs.
        services.AddHttpClient(GatewaySmsSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15)).RemoveAllLoggers();
        services.AddHttpClient(TelegramBotSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15)).RemoveAllLoggers();
        services.AddScoped<ISmsSender, GatewaySmsSender>();
        services.AddScoped<ITelegramSender, TelegramBotSender>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
