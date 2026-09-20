using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Infrastructure;

public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    private HttpContext? HttpContext => httpContextAccessor.HttpContext;

    public Guid? UserId => Guid.TryParse(HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) ? userId : null;
    public Guid? TenantId => Guid.TryParse(HttpContext?.User.FindFirst("tenant_id")?.Value, out var tenantId) ? tenantId : null;
    public string? Email => HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
    public string? IpAddress => HttpContext?.Connection.RemoteIpAddress?.ToString();
}
