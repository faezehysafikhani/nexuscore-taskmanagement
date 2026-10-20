namespace NexusCore.Application.Identity.Interfaces;

/// <summary>
/// Tasks given to posts (پُست سازمانی) instead of people - optional, off by default. A product's
/// organization structure (Nexus.OrgStructure, when turned on) implements it: a task keeps the
/// posts it is given to (its creator's, the responsible ones, those with access), and whoever
/// holds a post stands in for it, so when the holder changes the task moves with the post.
/// The task module keeps working with people: the posts' current holders.
/// </summary>
public interface ITaskPosts
{
    bool Enabled { get; }

    /// <summary>These posts as they are now (unknown ones are left out).</summary>
    Task<IReadOnlyDictionary<Guid, WorkPost>> GetPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> postIds, CancellationToken cancellationToken);

    /// <summary>Every post of the tenant, with its holder (for choosing whom to give a task).</summary>
    Task<IReadOnlyList<WorkPost>> ListPostsAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Each of these users' main post; users with no post are left out.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetMainPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>The posts tasks are given to.</summary>
    Task<IReadOnlyDictionary<Guid, TaskPostSet>> GetTaskPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken);

    /// <summary>
    /// Records the posts a task is given to. A null list leaves that part as it is; OwnerPostId is
    /// set only when the task has none yet.
    /// </summary>
    Task SetTaskPostsAsync(
        Guid tenantId, Guid taskId, Guid? ownerPostId, IReadOnlyList<Guid>? responsiblePostIds, IReadOnlyList<Guid>? accessPostIds,
        CancellationToken cancellationToken);
}

/// <summary>A post now: its holder (null: vacant), «کارشناس», its code and where it is.</summary>
public sealed record WorkPost(Guid PostId, Guid? HolderUserId, string Title, string Code, string Place, bool IsActive = true);

public sealed record TaskPostSet(Guid? OwnerPostId, IReadOnlyList<Guid> ResponsiblePostIds, IReadOnlyList<Guid> AccessPostIds);

public enum TaskPostRole
{
    Owner = 0,
    Responsible = 1,
    Access = 2
}

/// <summary>On a task: the person standing in for one of its posts changed (null: none).</summary>
public sealed record TaskPersonChange(Guid TaskId, TaskPostRole Role, Guid? FromUserId, Guid? ToUserId);

/// <summary>
/// Implemented by the task module: puts a post's new holder on its tasks in place of the old one
/// (as their creator, responsible or with access), and tells them.
/// </summary>
public interface ITaskPeopleUpdater
{
    Task ApplyAsync(Guid tenantId, IReadOnlyList<TaskPersonChange> changes, CancellationToken cancellationToken);

    /// <summary>Every task of the tenant with its creator, responsible people and access list.</summary>
    Task<IReadOnlyList<TaskPeopleSnapshot>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed record TaskPeopleSnapshot(Guid TaskId, Guid? OwnerUserId, IReadOnlyList<Guid> ResponsibleUserIds, IReadOnlyList<Guid> AccessUserIds);

/// <summary>The default: tasks are given to people.</summary>
public sealed class NoTaskPosts : ITaskPosts
{
    public bool Enabled => false;

    public Task<IReadOnlyDictionary<Guid, WorkPost>> GetPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> postIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, WorkPost>>(new Dictionary<Guid, WorkPost>());

    public Task<IReadOnlyList<WorkPost>> ListPostsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkPost>>([]);

    public Task<IReadOnlyDictionary<Guid, Guid>> GetMainPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(new Dictionary<Guid, Guid>());

    public Task<IReadOnlyDictionary<Guid, TaskPostSet>> GetTaskPostsAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, TaskPostSet>>(new Dictionary<Guid, TaskPostSet>());

    public Task SetTaskPostsAsync(
        Guid tenantId, Guid taskId, Guid? ownerPostId, IReadOnlyList<Guid>? responsiblePostIds, IReadOnlyList<Guid>? accessPostIds,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
