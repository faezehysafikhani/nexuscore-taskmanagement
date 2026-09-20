using Nexus.ProjectManagement.Deliverables.Application.Dtos;
using Nexus.ProjectManagement.Deliverables.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Deliverables.Application;

public sealed class DeliverableService(
    IDeliverableRepository repository,
    IDeliverablesUnitOfWork unitOfWork) : IDeliverableService
{
    public async Task<Result<IReadOnlyList<DeliverableDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var deliverables = await repository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<DeliverableDto>>(deliverables.Select(ToDto).ToList());
    }

    public async Task<Result<DeliverableDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var deliverable = await repository.GetByIdAsync(id, cancellationToken);
        return deliverable is null
            ? Result.Failure<DeliverableDto>(Error.NotFound("Deliverable not found."))
            : Result.Success(ToDto(deliverable));
    }

    public async Task<Result<DeliverableDto>> CreateAsync(CreateDeliverableRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<DeliverableDto>(Error.Validation("Title is required."));
        }

        var deliverable = new Deliverable(Guid.NewGuid(), request.TenantId, request.ProjectId, request.Title);
        deliverable.UpdateDetails(request.Title, request.Description, request.AcceptanceCriteria, request.ResponsibleUserId, request.TargetDate);

        await repository.AddAsync(deliverable, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(deliverable));
    }

    public async Task<Result<DeliverableDto>> UpdateAsync(Guid id, UpdateDeliverableRequest request, CancellationToken cancellationToken)
    {
        var deliverable = await repository.GetByIdAsync(id, cancellationToken);
        if (deliverable is null)
        {
            return Result.Failure<DeliverableDto>(Error.NotFound("Deliverable not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<DeliverableDto>(Error.Validation("Title is required."));
        }

        deliverable.UpdateDetails(request.Title, request.Description, request.AcceptanceCriteria, request.ResponsibleUserId, request.TargetDate);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(deliverable));
    }

    public async Task<Result<DeliverableDto>> ChangeStatusAsync(Guid id, ChangeDeliverableStatusRequest request, CancellationToken cancellationToken)
    {
        var deliverable = await repository.GetByIdAsync(id, cancellationToken);
        if (deliverable is null)
        {
            return Result.Failure<DeliverableDto>(Error.NotFound("Deliverable not found."));
        }

        deliverable.ChangeStatus(request.Status);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(deliverable));
    }

    private static DeliverableDto ToDto(Deliverable deliverable) => new(
        deliverable.Id, deliverable.TenantId, deliverable.ProjectId, deliverable.Title, deliverable.Description,
        deliverable.AcceptanceCriteria, deliverable.ResponsibleUserId, deliverable.TargetDate, deliverable.Status);
}
