namespace Nexus.Integrations.ProjectWorkflow.Application;

/// <summary>
/// The SubjectType strings the ProjectManagement family submits as ApprovalSubject.SubjectType.
/// Workflow itself treats SubjectType as an opaque string and knows nothing about this list -
/// it exists here, in the integration layer, purely so a Project-specific override can be
/// validated against a real, known set of PM subject types and so admin UIs have something to
/// list without hardcoding it themselves.
/// </summary>
public static class ProjectManagementSubjectTypes
{
    public const string Project = "Project";
    public const string WaterfallActivity = "WaterfallActivity";
    public const string AgileTask = "AgileTask";
    public const string Risk = "Risk";
    public const string Stakeholder = "Stakeholder";
    public const string ProgressUpdate = "ProgressUpdate";
    public const string ProjectDocument = "ProjectDocument";
    public const string Action = "Action";

    public static IReadOnlyList<string> All { get; } =
        [Project, WaterfallActivity, AgileTask, Risk, Stakeholder, ProgressUpdate, ProjectDocument, Action];
}
