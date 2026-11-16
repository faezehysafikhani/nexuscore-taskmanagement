using System.Globalization;
using System.Text.Json;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

public sealed record WorkTypeDto(Guid Id, string Name, string? Color, int SortOrder, bool IsActive);
public sealed record SaveWorkTypeRequest(string Name, string? Color = null, int SortOrder = 0, bool IsActive = true);

public sealed record CustomFieldDto(Guid Id, string Name, CustomFieldKind Kind, IReadOnlyList<string> Options, bool IsRequired, int SortOrder, bool IsActive);
public sealed record SaveCustomFieldRequest(string Name, CustomFieldKind Kind, IReadOnlyList<string>? Options = null, bool IsRequired = false, int SortOrder = 0, bool IsActive = true);

public sealed record TaskFieldValueDto(Guid FieldId, string Name, CustomFieldKind Kind, IReadOnlyList<string> Options, bool IsRequired, string? Value);
public sealed record TaskExtrasDto(Guid? WorkTypeId, string? WorkTypeName, IReadOnlyList<TaskFieldValueDto> Fields);
public sealed record SaveTaskExtrasRequest(Guid? WorkTypeId, IReadOnlyDictionary<Guid, string?>? Values);

public interface ITaskExtrasRepository
{
    Task<IReadOnlyList<WorkTypeDefinition>> ListWorkTypesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<WorkTypeDefinition?> GetWorkTypeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<WorkTypeDefinition?> GetWorkTypeByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken);
    Task AddWorkTypeAsync(WorkTypeDefinition item, CancellationToken cancellationToken);
    Task RemoveWorkTypeAsync(WorkTypeDefinition item, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomFieldDefinition>> ListFieldsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<CustomFieldDefinition?> GetFieldAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<CustomFieldDefinition?> GetFieldByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken);
    Task AddFieldAsync(CustomFieldDefinition item, CancellationToken cancellationToken);
    Task RemoveFieldAsync(CustomFieldDefinition item, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskFieldValue>> ListValuesAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken);
    Task AddValueAsync(TaskFieldValue value, CancellationToken cancellationToken);
    Task RemoveValueAsync(TaskFieldValue value, CancellationToken cancellationToken);
}

public interface ITaskExtrasService
{
    Task<Result<IReadOnlyList<WorkTypeDto>>> ListWorkTypesAsync(CancellationToken cancellationToken);
    Task<Result<WorkTypeDto>> CreateWorkTypeAsync(SaveWorkTypeRequest request, CancellationToken cancellationToken);
    Task<Result<WorkTypeDto>> UpdateWorkTypeAsync(Guid id, SaveWorkTypeRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteWorkTypeAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<CustomFieldDto>>> ListFieldsAsync(CancellationToken cancellationToken);
    Task<Result<CustomFieldDto>> CreateFieldAsync(SaveCustomFieldRequest request, CancellationToken cancellationToken);
    Task<Result<CustomFieldDto>> UpdateFieldAsync(Guid id, SaveCustomFieldRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteFieldAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<TaskExtrasDto>> GetForTaskAsync(Guid taskId, CancellationToken cancellationToken);
    Task<Result<TaskExtrasDto>> SetForTaskAsync(Guid taskId, SaveTaskExtrasRequest request, CancellationToken cancellationToken);
}

/// <summary>Work types (CAT-251) and custom fields (CAT-252): what an organization adds to a task beyond the built-in fields.</summary>
public sealed class TaskExtrasService(
    ITaskExtrasRepository repository,
    ITaskRepository taskRepository,
    ITaskService tasks,
    ITaskAccessScope access,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser) : ITaskExtrasService
{
    public const int MaxFields = 50;
    public const int MaxValueLength = 1000;

    private static WorkTypeDto ToDto(WorkTypeDefinition w) => new(w.Id, w.Name, w.Color, w.SortOrder, w.IsActive);

    private static IReadOnlyList<string> OptionsOf(CustomFieldDefinition f) =>
        string.IsNullOrWhiteSpace(f.OptionsJson) ? [] : JsonSerializer.Deserialize<List<string>>(f.OptionsJson) ?? [];

    private static CustomFieldDto ToDto(CustomFieldDefinition f) => new(f.Id, f.Name, f.Kind, OptionsOf(f), f.IsRequired, f.SortOrder, f.IsActive);

    // --- work types ---

    public async Task<Result<IReadOnlyList<WorkTypeDto>>> ListWorkTypesAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId) return Result.Failure<IReadOnlyList<WorkTypeDto>>(Error.Unauthorized());
        var list = await repository.ListWorkTypesAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<WorkTypeDto>>(list.Select(ToDto).ToList());
    }

    public async Task<Result<WorkTypeDto>> CreateWorkTypeAsync(SaveWorkTypeRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId) return Result.Failure<WorkTypeDto>(Error.Unauthorized());
        if (string.IsNullOrWhiteSpace(request.Name)) return Result.Failure<WorkTypeDto>(Error.Validation("نام نوع فعالیت را وارد کنید."));
        if (await repository.GetWorkTypeByNameAsync(tenantId, request.Name, cancellationToken) is not null)
            return Result.Failure<WorkTypeDto>(Error.Conflict("نوع فعالیتی با این نام از قبل وجود دارد."));

        var item = new WorkTypeDefinition(Guid.NewGuid(), tenantId, request.Name, request.Color, request.SortOrder);
        await repository.AddWorkTypeAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    public async Task<Result<WorkTypeDto>> UpdateWorkTypeAsync(Guid id, SaveWorkTypeRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId) return Result.Failure<WorkTypeDto>(Error.Unauthorized());
        var item = await repository.GetWorkTypeAsync(tenantId, id, cancellationToken);
        if (item is null) return Result.Failure<WorkTypeDto>(Error.NotFound("نوع فعالیت پیدا نشد."));
        if (string.IsNullOrWhiteSpace(request.Name)) return Result.Failure<WorkTypeDto>(Error.Validation("نام نوع فعالیت را وارد کنید."));
        var clash = await repository.GetWorkTypeByNameAsync(tenantId, request.Name, cancellationToken);
        if (clash is not null && clash.Id != id) return Result.Failure<WorkTypeDto>(Error.Conflict("نوع فعالیتی با این نام از قبل وجود دارد."));

        item.Update(request.Name, request.Color, request.SortOrder, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    public async Task<Result> DeleteWorkTypeAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId) return Result.Failure(Error.Unauthorized());
        var item = await repository.GetWorkTypeAsync(tenantId, id, cancellationToken);
        if (item is null) return Result.Failure(Error.NotFound("نوع فعالیت پیدا نشد."));
        await repository.RemoveWorkTypeAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // --- custom fields ---

    public async Task<Result<IReadOnlyList<CustomFieldDto>>> ListFieldsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId) return Result.Failure<IReadOnlyList<CustomFieldDto>>(Error.Unauthorized());
        var list = await repository.ListFieldsAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<CustomFieldDto>>(list.Select(ToDto).ToList());
    }

    private static Result? CheckField(SaveCustomFieldRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return Result.Failure(Error.Validation("نام فیلد را وارد کنید."));
        if (request.Kind == CustomFieldKind.Choice && (request.Options is null || request.Options.Count(o => !string.IsNullOrWhiteSpace(o)) < 2))
            return Result.Failure(Error.Validation("برای فیلد انتخابی دست‌کم دو گزینه لازم است."));
        return null;
    }

    private static string? OptionsJsonOf(SaveCustomFieldRequest request) =>
        request.Kind == CustomFieldKind.Choice
            ? JsonSerializer.Serialize(request.Options!.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToList())
            : null;

    public async Task<Result<CustomFieldDto>> CreateFieldAsync(SaveCustomFieldRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId) return Result.Failure<CustomFieldDto>(Error.Unauthorized());
        if (CheckField(request) is { } bad) return Result.Failure<CustomFieldDto>(bad.Error);
        if (await repository.GetFieldByNameAsync(tenantId, request.Name, cancellationToken) is not null)
            return Result.Failure<CustomFieldDto>(Error.Conflict("فیلدی با این نام از قبل وجود دارد."));
        if ((await repository.ListFieldsAsync(tenantId, cancellationToken)).Count >= MaxFields)
            return Result.Failure<CustomFieldDto>(Error.Validation("حداکثر ۵۰ فیلد سفارشی مجاز است."));

        var item = new CustomFieldDefinition(Guid.NewGuid(), tenantId, request.Name, request.Kind, OptionsJsonOf(request), request.IsRequired, request.SortOrder);
        await repository.AddFieldAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    public async Task<Result<CustomFieldDto>> UpdateFieldAsync(Guid id, SaveCustomFieldRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId) return Result.Failure<CustomFieldDto>(Error.Unauthorized());
        var item = await repository.GetFieldAsync(tenantId, id, cancellationToken);
        if (item is null) return Result.Failure<CustomFieldDto>(Error.NotFound("فیلد پیدا نشد."));
        var req = request with { Kind = item.Kind };
        if (CheckField(req) is { } bad) return Result.Failure<CustomFieldDto>(bad.Error);
        var clash = await repository.GetFieldByNameAsync(tenantId, request.Name, cancellationToken);
        if (clash is not null && clash.Id != id) return Result.Failure<CustomFieldDto>(Error.Conflict("فیلدی با این نام از قبل وجود دارد."));

        item.Update(req.Name, OptionsJsonOf(req), req.IsRequired, req.SortOrder, req.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(item));
    }

    public async Task<Result> DeleteFieldAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId) return Result.Failure(Error.Unauthorized());
        var item = await repository.GetFieldAsync(tenantId, id, cancellationToken);
        if (item is null) return Result.Failure(Error.NotFound("فیلد پیدا نشد."));
        await repository.RemoveFieldAsync(item, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // --- one task ---

    private async Task<TaskExtrasDto> BuildAsync(TaskItem task, CancellationToken cancellationToken)
    {
        var fields = (await repository.ListFieldsAsync(task.TenantId, cancellationToken)).Where(f => f.IsActive).ToList();
        var values = (await repository.ListValuesAsync(task.TenantId, task.Id, cancellationToken)).ToDictionary(v => v.FieldId);
        var typeName = task.WorkTypeId is { } wt ? (await repository.GetWorkTypeAsync(task.TenantId, wt, cancellationToken))?.Name : null;
        return new TaskExtrasDto(
            task.WorkTypeId,
            typeName,
            fields.Select(f => new TaskFieldValueDto(f.Id, f.Name, f.Kind, OptionsOf(f), f.IsRequired, values.GetValueOrDefault(f.Id)?.Value)).ToList());
    }

    public async Task<Result<TaskExtrasDto>> GetForTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId) return Result.Failure<TaskExtrasDto>(Error.Unauthorized());
        // The ordinary read decides who may see the task.
        var visible = await tasks.GetAsync(taskId, cancellationToken);
        if (visible.IsFailure) return Result.Failure<TaskExtrasDto>(visible.Error);
        var task = await taskRepository.GetByIdAsync(tenantId, taskId, cancellationToken);
        return task is null ? Result.Failure<TaskExtrasDto>(Error.NotFound("فعالیت پیدا نشد.")) : Result.Success(await BuildAsync(task, cancellationToken));
    }

    private static string? Normalize(CustomFieldDefinition field, string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return field.Kind switch
        {
            CustomFieldKind.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n.ToString(CultureInfo.InvariantCulture) : "!",
            CustomFieldKind.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "!",
            CustomFieldKind.Boolean => bool.TryParse(value, out var b) ? (b ? "true" : "false") : "!",
            CustomFieldKind.Choice => OptionsOf(field).Contains(value) ? value : "!",
            _ => value.Length <= MaxValueLength ? value : "!",
        };
    }

    public async Task<Result<TaskExtrasDto>> SetForTaskAsync(Guid taskId, SaveTaskExtrasRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId) return Result.Failure<TaskExtrasDto>(Error.Unauthorized());
        var task = await taskRepository.GetByIdAsync(tenantId, taskId, cancellationToken);
        if (task is null) return Result.Failure<TaskExtrasDto>(Error.NotFound("فعالیت پیدا نشد."));
        var allowed = access.CanManage(task) || (currentUser.UserId is { } who && task.IsResponsible(who));
        if (!allowed) return Result.Failure<TaskExtrasDto>(Error.Forbidden("فقط مالک یا مدیر فعالیت می‌تواند این را تغییر دهد."));

        if (request.WorkTypeId is { } typeId)
        {
            var type = await repository.GetWorkTypeAsync(tenantId, typeId, cancellationToken);
            if (type is null || (!type.IsActive && task.WorkTypeId != typeId)) return Result.Failure<TaskExtrasDto>(Error.Validation("نوع فعالیت انتخاب‌شده معتبر نیست."));
        }

        var fields = (await repository.ListFieldsAsync(tenantId, cancellationToken)).Where(f => f.IsActive).ToDictionary(f => f.Id);
        var existing = (await repository.ListValuesAsync(tenantId, taskId, cancellationToken)).ToDictionary(v => v.FieldId);
        var incoming = request.Values ?? new Dictionary<Guid, string?>();

        foreach (var (fieldId, raw) in incoming)
        {
            if (!fields.TryGetValue(fieldId, out var field)) return Result.Failure<TaskExtrasDto>(Error.Validation("فیلد انتخاب‌شده معتبر نیست."));
            if (Normalize(field, raw) == "!") return Result.Failure<TaskExtrasDto>(Error.Validation($"مقدار «{field.Name}» معتبر نیست."));
        }

        // A required field needs a value after this save: the one sent, or the one already stored.
        foreach (var field in fields.Values.Where(f => f.IsRequired))
        {
            var has = incoming.TryGetValue(field.Id, out var sent) ? Normalize(field, sent) is not null : existing.ContainsKey(field.Id);
            if (!has) return Result.Failure<TaskExtrasDto>(Error.Validation($"فیلد «{field.Name}» اجباری است."));
        }

        task.SetWorkType(request.WorkTypeId);
        foreach (var (fieldId, raw) in incoming)
        {
            var value = Normalize(fields[fieldId], raw);
            if (value is null)
            {
                if (existing.TryGetValue(fieldId, out var old)) await repository.RemoveValueAsync(old, cancellationToken);
            }
            else if (existing.TryGetValue(fieldId, out var old))
            {
                old.Set(value);
            }
            else
            {
                await repository.AddValueAsync(new TaskFieldValue(Guid.NewGuid(), tenantId, taskId, fieldId, value), cancellationToken);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await BuildAsync(task, cancellationToken));
    }
}
