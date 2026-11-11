using Microsoft.EntityFrameworkCore;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// Retention of this module's own logs (phase 9): the automation log and finished webhook deliveries are removed after the
/// days the organization chose; empty keeps them for ever. Tasks, time entries and the audit trail are never touched here.
/// </summary>
public sealed class RetentionService(IRetentionRepository repository, ITaskManagementUnitOfWork unitOfWork, ICurrentUserContext currentUser) : IRetentionService
{
    public const int MinDays = 7;
    public const int MaxDays = 3650;

    public async Task<Result<RetentionDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<RetentionDto>(Error.Unauthorized());
        }

        var row = await repository.GetAsync(tenantId, cancellationToken);
        return Result.Success(new RetentionDto(row?.AutomationLogDays, row?.WebhookDeliveryDays));
    }

    public async Task<Result<RetentionDto>> SetAsync(RetentionDto request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<RetentionDto>(Error.Unauthorized());
        }

        if (request.AutomationLogDays is < MinDays or > MaxDays || request.WebhookDeliveryDays is < MinDays or > MaxDays)
        {
            return Result.Failure<RetentionDto>(Error.Validation($"Choose between {MinDays} and {MaxDays} days, or leave empty to keep for ever."));
        }

        var row = await repository.GetAsync(tenantId, cancellationToken);
        if (row is null)
        {
            await repository.AddAsync(new TaskRetentionSetting(tenantId, request.AutomationLogDays, request.WebhookDeliveryDays), cancellationToken);
        }
        else
        {
            row.Set(request.AutomationLogDays, request.WebhookDeliveryDays);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(request);
    }
}

public interface IRetentionRepository
{
    Task<TaskRetentionSetting?> GetAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddAsync(TaskRetentionSetting setting, CancellationToken cancellationToken);
}
