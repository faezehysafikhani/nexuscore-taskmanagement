using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Application;

namespace Nexus.TaskManagement.Endpoints;

/// <summary>
/// Loads the caller's teams into <see cref="ITaskAccessScope"/> before the handler runs, so the
/// task query filter can include tasks assigned to those teams. Without it a restricted user
/// would still see their own and directly assigned tasks - never more.
/// </summary>
public sealed class TaskAccessScopeFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        await http.RequestServices.GetRequiredService<ITaskAccessScope>().EnsureLoadedAsync(http.RequestAborted);
        return await next(context);
    }
}
