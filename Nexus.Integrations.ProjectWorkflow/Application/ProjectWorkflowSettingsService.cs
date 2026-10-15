using Nexus.ProjectManagement.Core.Application;
using Nexus.Workflow.Application;
using Nexus.Workflow.Application.Dtos;
using Nexus.Workflow.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.ProjectWorkflow.Application;

public sealed class ProjectWorkflowSettingsService(
    IProjectRepository projectRepository,
    IWorkflowDefinitionService definitionService) : IProjectWorkflowSettingsService
{
    private const string ProjectScope = "Project";

    public async Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> GetAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken)
    {
        var listed = await definitionService.ListAsync(tenantId, subjectType: null, cancellationToken);
        return listed.IsFailure
            ? Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(listed.Error)
            : Result.Success(Project(listed.Value!, projectId));
    }

    public async Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> SaveGeneralAsync(
        Guid tenantId, string groupKey, SaveWorkflowGroupRequest request, CancellationToken cancellationToken) =>
        await SaveAsync(tenantId, null, groupKey, request, cancellationToken);

    public async Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> SaveProjectAsync(
        Guid tenantId, Guid projectId, string groupKey, SaveWorkflowGroupRequest request, Guid callerUserId, bool canConfigureAny, CancellationToken cancellationToken)
    {
        var allowed = await CheckProjectAsync(tenantId, projectId, callerUserId, canConfigureAny, cancellationToken);
        return allowed.IsFailure
            ? Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(allowed.Error)
            : await SaveAsync(tenantId, projectId, groupKey, request, cancellationToken);
    }

    private async Task<Result> CheckProjectAsync(Guid tenantId, Guid projectId, Guid callerUserId, bool canConfigureAny, CancellationToken cancellationToken)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.TenantId != tenantId)
        {
            return Result.Failure(Error.NotFound("Project was not found."));
        }

        return canConfigureAny || project.ManagerUserId == callerUserId || project.OwnerUserId == callerUserId
            ? Result.Success()
            : Result.Failure(Error.Forbidden("Only the project's manager or owner can change its approval rules."));
    }

    public async Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> ResetProjectAsync(
        Guid tenantId, Guid projectId, string groupKey, Guid callerUserId, bool canConfigureAny, CancellationToken cancellationToken)
    {
        var allowed = await CheckProjectAsync(tenantId, projectId, callerUserId, canConfigureAny, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(allowed.Error);
        }

        var group = ProjectWorkflowGroups.Find(groupKey);
        if (group is null)
        {
            return Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(Error.NotFound("Unknown workflow group."));
        }

        var listed = await definitionService.ListAsync(tenantId, subjectType: null, cancellationToken);
        if (listed.IsFailure)
        {
            return Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(listed.Error);
        }

        foreach (var definition in listed.Value!.Where(d =>
                     d.IsActive && d.ScopeType == ProjectScope && d.ScopeId == projectId && group.SubjectTypes.Contains(d.SubjectType)))
        {
            var reset = await definitionService.ResetToDefaultAsync(definition.Id, cancellationToken);
            if (reset.IsFailure)
            {
                return Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(reset.Error);
            }
        }

        return await GetAsync(tenantId, projectId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> ApplyDefaultsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var listed = await definitionService.ListAsync(tenantId, subjectType: null, cancellationToken);
        if (listed.IsFailure)
        {
            return Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(listed.Error);
        }

        foreach (var group in ProjectWorkflowGroups.All)
        {
            var general = FindGeneral(listed.Value!, group.SubjectTypes[0]);
            if (general is not null)
            {
                continue;
            }

            var saved = await WriteAsync(tenantId, null, group, group.Defaults.Select(ToSetting).ToList(), cancellationToken);
            if (saved.IsFailure)
            {
                return Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(saved.Error);
            }
        }

        return await GetAsync(tenantId, null, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> SaveAsync(
        Guid tenantId, Guid? projectId, string groupKey, SaveWorkflowGroupRequest request, CancellationToken cancellationToken)
    {
        var group = ProjectWorkflowGroups.Find(groupKey);
        if (group is null)
        {
            return Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(Error.NotFound("Unknown workflow group."));
        }

        var steps = request.Steps ?? group.Defaults.Select(ToSetting).ToList();
        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
            {
                return Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(Error.Validation("Step name is required."));
            }

            if (step.ApproverUserId is null == string.IsNullOrWhiteSpace(step.ApproverKey))
            {
                return Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(
                    Error.Validation("Each step needs exactly one approver: a user, or a participant key."));
            }

            if (step.ApproverKey is not null && !ApprovalParticipants.All.Contains(step.ApproverKey))
            {
                return Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(
                    Error.Validation($"Unknown approver key '{step.ApproverKey}'. Use one of: {string.Join(", ", ApprovalParticipants.All)}."));
            }
        }

        var saved = await WriteAsync(tenantId, projectId, group, steps, cancellationToken);
        return saved.IsFailure
            ? Result.Failure<IReadOnlyList<WorkflowGroupSettingsDto>>(saved.Error)
            : await GetAsync(tenantId, projectId, cancellationToken);
    }

    private async Task<Result> WriteAsync(
        Guid tenantId, Guid? projectId, WorkflowGroup group, IReadOnlyList<WorkflowStepSettingDto> steps, CancellationToken cancellationToken)
    {
        var inputs = steps
            .Select(step => new AddWorkflowStepRequest(
                step.Name, step.ApproverUserId, null, step.ApproverUserId is null ? step.ApproverKey : null, step.ApproverUserId is null && step.IsOptional))
            .ToList();

        foreach (var subjectType in group.SubjectTypes)
        {
            var saved = await definitionService.SetStepsAsync(
                new SetWorkflowStepsRequest(
                    tenantId, subjectType, projectId is null ? null : ProjectScope, projectId,
                    projectId is null ? $"{group.Key} approval" : $"{group.Key} approval (project)", inputs),
                cancellationToken);
            if (saved.IsFailure)
            {
                return Result.Failure(saved.Error);
            }
        }

        return Result.Success();
    }

    private static IReadOnlyList<WorkflowGroupSettingsDto> Project(IReadOnlyList<WorkflowDefinitionDto> definitions, Guid? projectId) =>
        ProjectWorkflowGroups.All.Select(group =>
        {
            var primary = group.SubjectTypes[0];
            var general = FindGeneral(definitions, primary);
            var own = projectId is null
                ? null
                : definitions.FirstOrDefault(d => d.IsActive && d.SubjectType == primary && d.ScopeType == ProjectScope && d.ScopeId == projectId);
            var effective = own ?? general;
            return new WorkflowGroupSettingsDto(
                group.Key, group.SubjectTypes, effective is not null, own is not null,
                effective is null ? [] : effective.Steps.OrderBy(s => s.Order).Select(s => new WorkflowStepSettingDto(s.Name, s.ApproverUserId, s.ApproverKey, s.IsOptional)).ToList(),
                group.Defaults.Select(ToSetting).ToList());
        }).ToList();

    private static WorkflowDefinitionDto? FindGeneral(IReadOnlyList<WorkflowDefinitionDto> definitions, string subjectType) =>
        definitions.FirstOrDefault(d => d.IsActive && d.SubjectType == subjectType && d.ScopeType == WorkflowDefinition.GeneralScope);

    private static WorkflowStepSettingDto ToSetting(DefaultWorkflowStep step) => new(step.Name, null, step.ApproverKey, step.IsOptional);
}
