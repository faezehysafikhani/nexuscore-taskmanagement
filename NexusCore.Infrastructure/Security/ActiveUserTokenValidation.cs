using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Interfaces;

namespace NexusCore.Infrastructure.Security;

/// <summary>
/// A valid signature is not enough: every request with an access token also checks that its
/// user still exists and is enabled (User.IsActive). Disabling an account therefore ends access
/// at once instead of when the access token expires; refresh tokens are revoked separately.
///
/// Added to the JWT bearer scheme of every host that calls AddInfrastructure, after the host's
/// own configuration, and chained behind a host's own OnTokenValidated handler.
/// </summary>
internal sealed class ActiveUserTokenValidation : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        options.Events ??= new JwtBearerEvents();
        var hostHandler = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            await hostHandler(context);
            if (context.Result is not null)
            {
                return;
            }

            var repository = context.HttpContext.RequestServices.GetRequiredService<IIdentityRepository>();
            if (!await IsActiveAsync(context.Principal, repository, context.HttpContext.RequestAborted))
            {
                context.Fail("The account is disabled or no longer exists.");
            }
        };
    }

    internal static async Task<bool> IsActiveAsync(ClaimsPrincipal? principal, IIdentityRepository repository, CancellationToken cancellationToken)
    {
        var subject = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(subject, out var userId) && await repository.IsUserActiveAsync(userId, cancellationToken);
    }
}
