using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Puts a title («آقای»، «خانم»، «جناب مهندس»...) in front of a user's display name, so every
/// place that shows the name shows it with the title. For modules that keep a list of titles
/// (Nexus.OrgStructure); nothing in the Core calls it, and it changes no table.
/// </summary>
public interface IUserTitleWriter
{
    /// <summary>
    /// The display name becomes "<paramref name="title"/> name" (just the name for a null title).
    /// The name is the user's first and last name, or - without them - the current display name
    /// with <paramref name="previousTitle"/> taken off its front.
    /// </summary>
    Task<Result> ApplyTitleAsync(Guid tenantId, Guid userId, string? previousTitle, string? title, CancellationToken cancellationToken);
}
