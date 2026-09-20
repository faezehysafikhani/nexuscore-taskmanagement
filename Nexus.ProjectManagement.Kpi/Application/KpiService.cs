using Nexus.ProjectManagement.Deliverables.Application;
using Nexus.ProjectManagement.Kpi.Application.Dtos;
using Nexus.ProjectManagement.Kpi.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Kpi.Application;

public sealed class KpiService(
    IKpiRepository repository,
    IDeliverableRepository deliverableRepository,
    IKpiUnitOfWork unitOfWork) : IKpiService
{
    public async Task<Result<IReadOnlyList<KpiDefinitionDto>>> ListByProjectAsync(Guid projectId, Guid? deliverableId, CancellationToken cancellationToken)
    {
        var kpis = await repository.ListByProjectAsync(projectId, deliverableId, cancellationToken);
        return Result.Success<IReadOnlyList<KpiDefinitionDto>>(kpis.Select(ToDto).ToList());
    }

    public async Task<Result<KpiDefinitionDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var kpi = await repository.GetByIdAsync(id, cancellationToken);
        return kpi is null
            ? Result.Failure<KpiDefinitionDto>(Error.NotFound("KPI not found."))
            : Result.Success(ToDto(kpi));
    }

    public async Task<Result<KpiDefinitionDto>> CreateAsync(CreateKpiDefinitionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<KpiDefinitionDto>(Error.Validation("Description is required."));
        }

        if (await deliverableRepository.GetByIdAsync(request.DeliverableId, cancellationToken) is null)
        {
            return Result.Failure<KpiDefinitionDto>(Error.Validation("Deliverable was not found."));
        }

        var kpi = new KpiDefinition(Guid.NewGuid(), request.TenantId, request.ProjectId, request.DeliverableId, request.Type, request.Description);
        kpi.UpdateDetails(request.Description, request.Formula, request.TargetValue);

        await repository.AddAsync(kpi, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(kpi));
    }

    public async Task<Result<KpiDefinitionDto>> UpdateAsync(Guid id, UpdateKpiDefinitionRequest request, CancellationToken cancellationToken)
    {
        var kpi = await repository.GetByIdAsync(id, cancellationToken);
        if (kpi is null)
        {
            return Result.Failure<KpiDefinitionDto>(Error.NotFound("KPI not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<KpiDefinitionDto>(Error.Validation("Description is required."));
        }

        kpi.UpdateDetails(request.Description, request.Formula, request.TargetValue);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(kpi));
    }

    private static KpiDefinitionDto ToDto(KpiDefinition kpi) => new(
        kpi.Id, kpi.TenantId, kpi.ProjectId, kpi.DeliverableId, kpi.Type, kpi.Description, kpi.Formula, kpi.TargetValue);
}
