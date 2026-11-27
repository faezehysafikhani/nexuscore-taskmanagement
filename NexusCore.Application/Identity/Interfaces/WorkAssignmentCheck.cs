using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Whether the signed-in user may make someone responsible for work - an action's responsible person, a project's
/// manager - by the same rules as a task's (<see cref="IWorkAssignmentScope"/>, <see cref="IUserReachScope"/>):
/// where the organization structure turns them on, only people of one's own company, at one's own level or below.
/// Only a newly chosen person is checked: whoever is already there stays, and anyone may name themselves.
/// </summary>
public static class WorkAssignmentCheck
{
    public static async Task<Error?> CheckAsync(
        IWorkAssignmentScope? assignmentScope,
        IUserReachScope? reachScope,
        ICurrentUserContext? currentUser,
        Guid tenantId,
        Guid? chosenUserId,
        Guid? currentUserIdOnRecord,
        string role,
        CancellationToken cancellationToken)
    {
        if (chosenUserId is not { } chosen || chosen == currentUserIdOnRecord || currentUser?.UserId is not { } me || chosen == me)
        {
            return null;
        }

        if (reachScope is not null && await reachScope.GetReachAsync(tenantId, me, cancellationToken) is { } reach && !reach.Contains(chosen))
        {
            return Error.Validation($"{role} را فقط از افراد شرکت خودتان می‌توانید انتخاب کنید.");
        }

        if (assignmentScope is not null && await assignmentScope.GetLimitAsync(tenantId, me, cancellationToken) is { } limit
            && limit.BlockedUserIds.Contains(chosen))
        {
            return Error.Validation($"{role} را فقط از هم‌سطح یا پایین‌تر از خودتان در ساختار سازمانی می‌توانید انتخاب کنید.");
        }

        return null;
    }
}

/// <summary>Whom the signed-in user may not make responsible for work, for the pickers (the server checks again).</summary>
/// <param name="Limited">Whether the level rule applies to the user at all.</param>
/// <param name="BlockedUserIds">People who may not be chosen (above the user, or of another company).</param>
/// <param name="AllowedUserIds">Where kept to one's own company: its people; null when not limited.</param>
public sealed record WorkAssignmentLimitDto(bool Limited, IReadOnlyList<Guid> BlockedUserIds, IReadOnlyList<Guid>? AllowedUserIds);
