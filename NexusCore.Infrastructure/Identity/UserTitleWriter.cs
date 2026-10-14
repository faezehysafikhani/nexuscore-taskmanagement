using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Infrastructure.Persistence;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Identity;

public sealed class UserTitleWriter(NexusCoreDbContext dbContext) : IUserTitleWriter
{
    public async Task<Result> ApplyTitleAsync(Guid tenantId, Guid userId, string? previousTitle, string? title, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("User was not found."));
        }

        // The built-in administrator cannot be renamed through the API; a title is no exception.
        if (user.IsSystem)
        {
            return Result.Failure(Error.Validation("The system administrator's name cannot be changed."));
        }

        var name = ComposeName(user.FirstName, user.LastName, user.DisplayName, previousTitle);
        var displayName = string.IsNullOrWhiteSpace(title) ? name : $"{title.Trim()} {name}";
        if (displayName != user.DisplayName)
        {
            user.ChangeDisplayName(displayName);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>The name without any title.</summary>
    public static string ComposeName(string? firstName, string? lastName, string displayName, string? previousTitle)
    {
        var full = $"{firstName} {lastName}".Trim();
        if (full.Length > 0)
        {
            return full;
        }

        var prefix = previousTitle?.Trim();
        return !string.IsNullOrEmpty(prefix) && displayName.StartsWith(prefix + " ", StringComparison.Ordinal)
            ? displayName[(prefix.Length + 1)..].Trim()
            : displayName.Trim();
    }
}
