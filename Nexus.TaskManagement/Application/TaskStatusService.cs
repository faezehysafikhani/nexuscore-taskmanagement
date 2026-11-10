using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

public sealed class TaskStatusService(
    ITaskStatusRepository repository,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser) : ITaskStatusService
{
    public static TaskStatusDto ToDto(TaskStatusDefinition s) => new(s.Id, s.Name, s.Category, s.Color, s.SortOrder, s.IsActive);

    public async Task<Result<IReadOnlyList<TaskStatusDto>>> ListAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<IReadOnlyList<TaskStatusDto>>(Error.Unauthorized());
        }

        var list = await repository.ListAsync(currentUser.TenantId.Value, cancellationToken);
        return Result.Success<IReadOnlyList<TaskStatusDto>>(list.Select(ToDto).ToList());
    }

    public async Task<Result<TaskStatusDto>> CreateAsync(CreateTaskStatusRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TaskStatusDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        if (await repository.GetByNameAsync(tenantId, request.Name, cancellationToken) is not null)
        {
            return Result.Failure<TaskStatusDto>(Error.Conflict("A status with this name already exists."));
        }

        var status = new TaskStatusDefinition(Guid.NewGuid(), tenantId, request.Name, request.Category, request.Color, request.SortOrder);
        await repository.AddAsync(status, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(status));
    }

    public async Task<Result<TaskStatusDto>> UpdateAsync(Guid id, UpdateTaskStatusRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure<TaskStatusDto>(Error.Unauthorized());
        }

        var tenantId = currentUser.TenantId.Value;
        var status = await repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (status is null)
        {
            return Result.Failure<TaskStatusDto>(Error.NotFound("Status not found."));
        }

        var clash = await repository.GetByNameAsync(tenantId, request.Name, cancellationToken);
        if (clash is not null && clash.Id != id)
        {
            return Result.Failure<TaskStatusDto>(Error.Conflict("A status with this name already exists."));
        }

        // Changing the category moves the tasks that show this status only the next time their status is
        // set; their stored category stays as it was, so a report never changes under a task's feet.
        status.Update(request.Name, request.Category, request.Color, request.SortOrder, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(status));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var status = await repository.GetByIdAsync(currentUser.TenantId.Value, id, cancellationToken);
        if (status is null)
        {
            return Result.Failure(Error.NotFound("Status not found."));
        }

        await repository.RemoveAsync(status, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
