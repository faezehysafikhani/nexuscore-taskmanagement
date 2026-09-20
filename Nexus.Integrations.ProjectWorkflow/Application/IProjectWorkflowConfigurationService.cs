using Nexus.Workflow.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.ProjectWorkflow.Application;

public sealed record CreateProjectWorkflowOverrideRequest(Guid TenantId, Guid ProjectId, string SubjectType, string Name);

/// <summary>The "Project-specific Workflow" feature: create/list WorkflowDefinitions scoped to
/// one Project. Everything else (steps, approval, General fallback) is plain Workflow - this
/// service only adds what Workflow itself cannot do: validate the ScopeId is a real Project.</summary>
public interface IProjectWorkflowConfigurationService
{
    Task<Result<WorkflowDefinitionDto>> CreateProjectOverrideAsync(CreateProjectWorkflowOverrideRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<WorkflowDefinitionDto>>> ListProjectOverridesAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken);
}
