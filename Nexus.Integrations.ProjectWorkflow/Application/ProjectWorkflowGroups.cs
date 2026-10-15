using NexusCore.Application.Approvals;

namespace Nexus.Integrations.ProjectWorkflow.Application;

public sealed record DefaultWorkflowStep(string Name, string ApproverKey, bool IsOptional = false);

/// <summary>
/// The things a project's approval rules are configured for. A group is what an administrator edits as one
/// unit ("risks"); it covers every subject type that is approved the same way (a risk and a risk-response report),
/// so one saved chain is applied to all of them. Defaults are what "apply standard settings" creates.
/// </summary>
public sealed record WorkflowGroup(string Key, IReadOnlyList<string> SubjectTypes, IReadOnlyList<DefaultWorkflowStep> Defaults);

public static class ProjectWorkflowGroups
{
    private static readonly DefaultWorkflowStep[] FinalApprovalByManager = [new("تایید نهایی", ApprovalParticipants.Manager)];

    public static IReadOnlyList<WorkflowGroup> All { get; } =
    [
        new("charter", [ProjectManagementSubjectTypes.Project], FinalApprovalByManager),
        new("activity_progress",
            [ProjectManagementSubjectTypes.ActivityProgress, ProjectManagementSubjectTypes.AgileTaskStatus,
             ProjectManagementSubjectTypes.WaterfallActivity, ProjectManagementSubjectTypes.AgileTask],
            FinalApprovalByManager),
        new("risks", [ProjectManagementSubjectTypes.Risk, ProjectManagementSubjectTypes.RiskResponseStatus], FinalApprovalByManager),
        new("stakeholders", [ProjectManagementSubjectTypes.Stakeholder], FinalApprovalByManager),
        new("status_updates", [ProjectManagementSubjectTypes.ProgressUpdate], FinalApprovalByManager),
        new("delay_reasons", [ProjectManagementSubjectTypes.DelayReason], FinalApprovalByManager),
        new("documents", [ProjectManagementSubjectTypes.ProjectDocument],
            [new("تایید اولیه", ApprovalParticipants.Manager), new("تایید نهایی", ApprovalParticipants.Owner)]),
        new("contracts", [ProjectManagementSubjectTypes.Contract, ProjectManagementSubjectTypes.ContractAddendum], FinalApprovalByManager),
        new("invoices", [ProjectManagementSubjectTypes.ContractInvoice], FinalApprovalByManager),
        // An action's status changes go through the approvers named on the action itself (up to three, in order).
        new("actions", [ProjectManagementSubjectTypes.Action, ProjectManagementSubjectTypes.ActionStatus],
            [new("تایید مسئول", ApprovalParticipants.Approver1),
             new("تایید دوم", ApprovalParticipants.Approver2, IsOptional: true),
             new("تایید سوم", ApprovalParticipants.Approver3, IsOptional: true)])
    ];

    public static WorkflowGroup? Find(string key) => All.FirstOrDefault(g => g.Key == key);
}
