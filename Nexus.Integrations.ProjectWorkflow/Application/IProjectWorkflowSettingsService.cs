using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.ProjectWorkflow.Application;

/// <summary>One step of an approval chain as an administrator edits it: either a fixed user or a participant
/// key (Manager, Owner, Approver1..3), and whether the step is skipped when that participant is unknown.</summary>
public sealed record WorkflowStepSettingDto(string Name, Guid? ApproverUserId, string? ApproverKey, bool IsOptional = false);

/// <summary>The chain that applies to one group, for the general rules or for one project.
/// IsConfigured false: nothing is defined, so approvals in this group are not required (they approve directly);
/// DefaultSteps is what "apply standard settings" would create. IsCustom: this project has its own chain.</summary>
public sealed record WorkflowGroupSettingsDto(
    string Key,
    IReadOnlyList<string> SubjectTypes,
    bool IsConfigured,
    bool IsCustom,
    IReadOnlyList<WorkflowStepSettingDto> Steps,
    IReadOnlyList<WorkflowStepSettingDto> DefaultSteps);

/// <summary>Steps null = the standard chain for the group.</summary>
public sealed record SaveWorkflowGroupRequest(IReadOnlyList<WorkflowStepSettingDto>? Steps);

public interface IProjectWorkflowSettingsService
{
    /// <summary>The effective chain of every group; with a project, its own chain where it has one.</summary>
    Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> GetAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> SaveGeneralAsync(Guid tenantId, string groupKey, SaveWorkflowGroupRequest request, CancellationToken cancellationToken);

    /// <summary>A project's own chain. Whoever may configure workflows can do it for any project; otherwise only the
    /// project's manager or owner can (<paramref name="canConfigureAny"/> false, <paramref name="callerUserId"/> is theirs).</summary>
    Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> SaveProjectAsync(
        Guid tenantId, Guid projectId, string groupKey, SaveWorkflowGroupRequest request, Guid callerUserId, bool canConfigureAny, CancellationToken cancellationToken);

    /// <summary>Drops the project's own chain for the group; it follows the general rules again. Same who-may rule as saving.</summary>
    Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> ResetProjectAsync(
        Guid tenantId, Guid projectId, string groupKey, Guid callerUserId, bool canConfigureAny, CancellationToken cancellationToken);

    /// <summary>Creates the standard chain for every group that has no general rules yet; leaves the others alone.</summary>
    Task<Result<IReadOnlyList<WorkflowGroupSettingsDto>>> ApplyDefaultsAsync(Guid tenantId, CancellationToken cancellationToken);
}
