using NexusCore.SharedKernel.Domain;

namespace Nexus.Workflow.Domain;

/// <summary>
/// Fully generic - knows nothing about Project Management or any other domain module.
/// SubjectType is an opaque string ("Project", "Risk", "WaterfallActivity", ...) supplied by
/// whatever module submits an ApprovalSubject. ScopeType/ScopeId let one subject type carry a
/// General definition plus optional scope-specific overrides (e.g. "Project"/{projectId}) -
/// Workflow only ever compares these as opaque values, it never validates what a ScopeId
/// refers to (that validation, when it matters, belongs to the module that owns the scope -
/// see Nexus.Integrations.ProjectWorkflow).
/// </summary>
public sealed class WorkflowDefinition : AuditableEntity<Guid>
{
    public const string GeneralScope = "General";

    private readonly List<WorkflowStep> _steps = [];

    private WorkflowDefinition() : base(Guid.Empty)
    {
        Name = string.Empty;
        SubjectType = string.Empty;
        ScopeType = GeneralScope;
    }

    public WorkflowDefinition(Guid id, Guid tenantId, string name, string subjectType, string? scopeType = null, Guid? scopeId = null) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        SubjectType = subjectType.Trim();
        ScopeType = string.IsNullOrWhiteSpace(scopeType) ? GeneralScope : scopeType.Trim();
        ScopeId = scopeId;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string SubjectType { get; private set; }
    public string ScopeType { get; private set; }
    public Guid? ScopeId { get; private set; }
    public bool IsActive { get; private set; }
    /// <summary>Not sorted here - _steps must stay the exact EF-tracked backing collection so
    /// Include() can populate it. Callers that need step order should OrderBy(Order).</summary>
    public IReadOnlyCollection<WorkflowStep> Steps => _steps.AsReadOnly();

    public void Rename(string name) => Name = name.Trim();

    public void SetActive(bool isActive) => IsActive = isActive;

    public WorkflowStep AddStep(Guid stepId, string name, Guid? approverUserId, Guid? approverRoleId)
    {
        var order = _steps.Count == 0 ? 1 : _steps.Max(step => step.Order) + 1;
        var step = new WorkflowStep(stepId, Id, order, name, approverUserId, approverRoleId);
        _steps.Add(step);
        return step;
    }

    public void DeleteStep(Guid stepId)
    {
        _steps.RemoveAll(step => step.Id == stepId);
        Renumber();
    }

    public void MoveStep(Guid stepId, int newOrder)
    {
        var step = _steps.SingleOrDefault(s => s.Id == stepId) ?? throw new InvalidOperationException("Step not found.");
        var ordered = _steps.Where(s => s.Id != stepId).OrderBy(s => s.Order).ToList();
        var insertAt = Math.Clamp(newOrder - 1, 0, ordered.Count);
        ordered.Insert(insertAt, step);

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SetOrder(i + 1);
        }
    }

    private void Renumber()
    {
        var ordered = _steps.OrderBy(step => step.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SetOrder(i + 1);
        }
    }
}
