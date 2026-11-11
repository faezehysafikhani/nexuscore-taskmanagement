using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Permissions;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Requests (phase 5): anyone with Requests.Submit asks for something; it is routed by its category (and urgency) and a
/// Requests.Handle holder turns it into a task - through <see cref="ITaskService"/>, so every task rule applies - or rejects it.
/// The task keeps the request as its source, and the requester is on its access list to follow it.
/// </summary>
public sealed class TaskRequestService(
    ITaskRequestRepository repository,
    ITaskRepository taskRepository,
    ITaskService tasks,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser) : ITaskRequestService
{
    public const string SourceType = "Request";

    private static RequestCategoryDto ToDto(RequestCategory c) => new(c.Id, c.Name, c.RouteToUserId, c.UrgentRouteToUserId, c.DefaultPriority, c.IsActive);

    // --- categories ---

    public async Task<Result<IReadOnlyList<RequestCategoryDto>>> ListCategoriesAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<RequestCategoryDto>>(Error.Unauthorized());
        }

        var list = await repository.ListCategoriesAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<RequestCategoryDto>>(list.Select(ToDto).ToList());
    }

    public async Task<Result<RequestCategoryDto>> CreateCategoryAsync(SaveRequestCategoryRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<RequestCategoryDto>(Error.Unauthorized());
        }

        if (await repository.GetCategoryByNameAsync(tenantId, request.Name, cancellationToken) is not null)
        {
            return Result.Failure<RequestCategoryDto>(Error.Conflict("A category with this name already exists."));
        }

        var category = new RequestCategory(Guid.NewGuid(), tenantId, request.Name, request.RouteToUserId, request.UrgentRouteToUserId, request.DefaultPriority);
        await repository.AddCategoryAsync(category, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(category));
    }

    public async Task<Result<RequestCategoryDto>> UpdateCategoryAsync(Guid id, SaveRequestCategoryRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<RequestCategoryDto>(Error.Unauthorized());
        }

        var category = await repository.GetCategoryAsync(tenantId, id, cancellationToken);
        if (category is null)
        {
            return Result.Failure<RequestCategoryDto>(Error.NotFound("Category not found."));
        }

        var clash = await repository.GetCategoryByNameAsync(tenantId, request.Name, cancellationToken);
        if (clash is not null && clash.Id != id)
        {
            return Result.Failure<RequestCategoryDto>(Error.Conflict("A category with this name already exists."));
        }

        category.Update(request.Name, request.RouteToUserId, request.UrgentRouteToUserId, request.DefaultPriority, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(category));
    }

    // --- requests ---

    public async Task<Result<TaskRequestDto>> SubmitAsync(SubmitTaskRequestRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<TaskRequestDto>(Error.Unauthorized());
        }

        RequestCategory? category = null;
        if (request.CategoryId is { } categoryId)
        {
            category = await repository.GetCategoryAsync(tenantId, categoryId, cancellationToken);
            if (category is null || !category.IsActive)
            {
                return Result.Failure<TaskRequestDto>(Error.Validation("Unknown or inactive category."));
            }
        }

        var priority = request.Priority ?? category?.DefaultPriority ?? TaskPriority.Medium;
        var item = new TaskRequest(
            Guid.NewGuid(), tenantId, category?.Id, request.Title, request.Description?.Trim(), priority, userId,
            category?.RouteFor(priority), request.NeededBy);
        await repository.AddAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(item, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<TaskRequestDto>>> ListAsync(bool mine, TaskRequestStatus? status, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<IReadOnlyList<TaskRequestDto>>(Error.Unauthorized());
        }

        if (!mine && !currentUser.HasPermission(TaskManagementPermissions.HandleRequests))
        {
            return Result.Failure<IReadOnlyList<TaskRequestDto>>(Error.Forbidden("Only a request handler can see every request."));
        }

        var items = await repository.ListAsync(tenantId, mine ? userId : null, status, cancellationToken);
        var dtos = new List<TaskRequestDto>();
        var people = await PeopleAsync(items, cancellationToken);
        var categories = (await repository.ListCategoriesAsync(tenantId, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        foreach (var item in items)
        {
            dtos.Add(ToDto(item, people, categories));
        }

        return Result.Success<IReadOnlyList<TaskRequestDto>>(dtos);
    }

    public async Task<Result<TaskDto>> ConvertAsync(Guid id, ConvertTaskRequestRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<TaskDto>(Error.Unauthorized());
        }

        var item = await repository.GetAsync(tenantId, id, cancellationToken);
        if (item is null)
        {
            return Result.Failure<TaskDto>(Error.NotFound("Request not found."));
        }

        if (item.Status != TaskRequestStatus.New)
        {
            return Result.Failure<TaskDto>(Error.Conflict("This request has already been decided."));
        }

        var assignee = request.AssignedUserId ?? item.RoutedToUserId ?? userId;
        var created = await tasks.CreateAsync(
            new CreateTaskRequest(
                item.Title,
                request.DueDate ?? item.NeededBy ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                item.Priority,
                item.Description,
                AssignedUserId: assignee,
                // The requester follows what became of the request.
                AssigneeUserIds: item.RequesterUserId == assignee ? null : [item.RequesterUserId]),
            cancellationToken);
        if (created.IsFailure)
        {
            return created;
        }

        var task = await taskRepository.GetForUpdateAsync(tenantId, created.Value.Id, cancellationToken);
        task?.SetSource(SourceType, item.Id);
        item.Convert(created.Value.Id, userId, DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(created.Value with { SourceType = SourceType, SourceId = item.Id });
    }

    public async Task<Result<TaskRequestDto>> RejectAsync(Guid id, RejectTaskRequestRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || currentUser.UserId is not { } userId)
        {
            return Result.Failure<TaskRequestDto>(Error.Unauthorized());
        }

        var item = await repository.GetAsync(tenantId, id, cancellationToken);
        if (item is null)
        {
            return Result.Failure<TaskRequestDto>(Error.NotFound("Request not found."));
        }

        if (item.Status != TaskRequestStatus.New)
        {
            return Result.Failure<TaskRequestDto>(Error.Conflict("This request has already been decided."));
        }

        item.Reject(string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), userId, DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(item, cancellationToken));
    }

    private async Task<IReadOnlyDictionary<Guid, UserSummaryDto>> PeopleAsync(IEnumerable<TaskRequest> items, CancellationToken cancellationToken)
    {
        var ids = items.SelectMany(i => new[] { i.RequesterUserId, i.RoutedToUserId ?? Guid.Empty }).Where(g => g != Guid.Empty).Distinct().ToList();
        return await taskRepository.GetUserSummariesAsync(ids, cancellationToken);
    }

    private async Task<TaskRequestDto> ToDtoAsync(TaskRequest item, CancellationToken cancellationToken)
    {
        var people = await PeopleAsync([item], cancellationToken);
        var categories = new Dictionary<Guid, string>();
        if (item.CategoryId is { } categoryId && await repository.GetCategoryAsync(item.TenantId, categoryId, cancellationToken) is { } category)
        {
            categories[category.Id] = category.Name;
        }

        return ToDto(item, people, categories);
    }

    private static TaskRequestDto ToDto(TaskRequest i, IReadOnlyDictionary<Guid, UserSummaryDto> people, IReadOnlyDictionary<Guid, string> categories) => new(
        i.Id, i.CategoryId, i.CategoryId is { } c && categories.TryGetValue(c, out var name) ? name : null,
        i.Title, i.Description, i.Priority, i.NeededBy,
        people.TryGetValue(i.RequesterUserId, out var requester) ? requester : null,
        i.RoutedToUserId is { } to && people.TryGetValue(to, out var routed) ? routed : null,
        i.Status, i.DecisionNote, i.ConvertedTaskId, i.CreatedAtUtc, i.DecidedAtUtc);
}
