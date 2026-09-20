using Nexus.ProjectManagement.Core.Application;
using Nexus.Workflow.Application;
using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.ProjectWorkflow.Application;

public sealed class ProjectWorkflowConfigurationService(
    IProjectRepository projectRepository,
    IWorkflowDefinitionService workflowDefinitionService) : IProjectWorkflowConfigurationService
{
    public async Task<Result<WorkflowDefinitionDto>> CreateProjectOverrideAsync(CreateProjectWorkflowOverrideRequest request, CancellationToken cancellationToken)
    {
        if (await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken) is null)
        {
            return Result.Failure<WorkflowDefinitionDto>(Error.Validation("Project was not found."));
        }

        if (!ProjectManagementSubjectTypes.All.Contains(request.SubjectType))
        {
            return Result.Failure<WorkflowDefinitionDto>(Error.Validation("Unknown Project Management subject type."));
        }

        var createRequest = new CreateWorkflowDefinitionRequest(request.TenantId, request.Name, request.SubjectType, "Project", request.ProjectId);
        return await workflowDefinitionService.CreateAsync(createRequest, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<WorkflowDefinitionDto>>> ListProjectOverridesAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var allDefinitions = await workflowDefinitionService.ListAsync(tenantId, subjectType: null, cancellationToken);
        if (allDefinitions.IsFailure)
        {
            return Result.Failure<IReadOnlyList<WorkflowDefinitionDto>>(allDefinitions.Error);
        }

        var scoped = allDefinitions.Value!
            .Where(definition => definition.ScopeType == "Project" && definition.ScopeId == projectId)
            .ToList();

        return Result.Success<IReadOnlyList<WorkflowDefinitionDto>>(scoped);
    }
}
