using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.Workflow.Application;

public sealed class WorkflowDefinitionService(
    IWorkflowDefinitionRepository repository,
    IWorkflowUnitOfWork unitOfWork) : IWorkflowDefinitionService
{
    public async Task<Result<IReadOnlyList<WorkflowDefinitionDto>>> ListAsync(Guid tenantId, string? subjectType, CancellationToken cancellationToken)
    {
        var definitions = await repository.ListAsync(tenantId, subjectType, cancellationToken);
        return Result.Success<IReadOnlyList<WorkflowDefinitionDto>>(definitions.Select(ToDto).ToList());
    }

    public async Task<Result<WorkflowDefinitionDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var definition = await repository.GetByIdAsync(id, cancellationToken);
        return definition is null
            ? Result.Failure<WorkflowDefinitionDto>(Error.NotFound("Workflow definition not found."))
            : Result.Success(ToDto(definition));
    }

    public async Task<Result<WorkflowDefinitionDto>> CreateAsync(CreateWorkflowDefinitionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.SubjectType))
        {
            return Result.Failure<WorkflowDefinitionDto>(Error.Validation("Name and subject type are required."));
        }

        var definition = new WorkflowDefinition(Guid.NewGuid(), request.TenantId, request.Name, request.SubjectType, request.ScopeType, request.ScopeId);
        await repository.AddAsync(definition, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(definition));
    }

    public async Task<Result<WorkflowDefinitionDto>> AddStepAsync(Guid id, AddWorkflowStepRequest request, CancellationToken cancellationToken)
    {
        var definition = await repository.GetByIdAsync(id, cancellationToken);
        if (definition is null)
        {
            return Result.Failure<WorkflowDefinitionDto>(Error.NotFound("Workflow definition not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<WorkflowDefinitionDto>(Error.Validation("Step name is required."));
        }

        definition.AddStep(Guid.NewGuid(), request.Name, request.ApproverUserId, request.ApproverRoleId, request.ApproverKey, request.IsOptional);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(definition));
    }

    public async Task<Result<WorkflowDefinitionDto>> DeleteStepAsync(Guid id, Guid stepId, CancellationToken cancellationToken)
    {
        var definition = await repository.GetByIdAsync(id, cancellationToken);
        if (definition is null)
        {
            return Result.Failure<WorkflowDefinitionDto>(Error.NotFound("Workflow definition not found."));
        }

        definition.DeleteStep(stepId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(definition));
    }

    public async Task<Result<WorkflowDefinitionDto>> MoveStepAsync(Guid id, Guid stepId, MoveWorkflowStepRequest request, CancellationToken cancellationToken)
    {
        var definition = await repository.GetByIdAsync(id, cancellationToken);
        if (definition is null)
        {
            return Result.Failure<WorkflowDefinitionDto>(Error.NotFound("Workflow definition not found."));
        }

        definition.MoveStep(stepId, request.NewOrder);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(definition));
    }

    public async Task<Result<WorkflowDefinitionDto>> SetStepsAsync(SetWorkflowStepsRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectType))
        {
            return Result.Failure<WorkflowDefinitionDto>(Error.Validation("Subject type is required."));
        }

        if (request.Steps.Any(step => string.IsNullOrWhiteSpace(step.Name)))
        {
            return Result.Failure<WorkflowDefinitionDto>(Error.Validation("Step name is required."));
        }

        var scopeType = string.IsNullOrWhiteSpace(request.ScopeType) ? WorkflowDefinition.GeneralScope : request.ScopeType.Trim();
        var definition = await repository.FindExactAsync(request.TenantId, request.SubjectType.Trim(), scopeType, request.ScopeId, cancellationToken);
        if (definition is null)
        {
            var name = string.IsNullOrWhiteSpace(request.Name) ? $"{request.SubjectType.Trim()} approval" : request.Name;
            definition = new WorkflowDefinition(Guid.NewGuid(), request.TenantId, name, request.SubjectType, scopeType, request.ScopeId);
            await repository.AddAsync(definition, cancellationToken);
        }
        else
        {
            definition.SetActive(true);
        }

        definition.ReplaceSteps(request.Steps.Select(step =>
            (step.Name, step.ApproverUserId, step.ApproverRoleId, step.ApproverKey, step.IsOptional)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(definition));
    }

    public async Task<Result> ResetToDefaultAsync(Guid id, CancellationToken cancellationToken)
    {
        var definition = await repository.GetByIdAsync(id, cancellationToken);
        if (definition is null)
        {
            return Result.Failure(Error.NotFound("Workflow definition not found."));
        }

        if (definition.ScopeType == WorkflowDefinition.GeneralScope)
        {
            return Result.Failure(Error.Validation("The General definition cannot be reset - delete or edit its steps instead."));
        }

        definition.SetActive(false);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static WorkflowDefinitionDto ToDto(WorkflowDefinition definition) => new(
        definition.Id, definition.TenantId, definition.Name, definition.SubjectType, definition.ScopeType, definition.ScopeId, definition.IsActive,
        definition.Steps.OrderBy(s => s.Order).Select(s => new WorkflowStepDto(s.Id, s.Order, s.Name, s.ApproverUserId, s.ApproverRoleId, s.ApproverKey, s.IsOptional)).ToList());
}
