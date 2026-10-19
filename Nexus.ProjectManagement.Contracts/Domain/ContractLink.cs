using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Contracts.Domain;

/// <summary>What a link hangs on. Stored as its integer value; never reorder or renumber the members.</summary>
public enum ContractLinkSubject
{
    Contract = 0,
    Milestone = 1,
    Obligation = 2
}

/// <summary>What a link points at. Stored as its integer value; never reorder or renumber the members.</summary>
public enum ContractLinkKind
{
    Document = 0,
    Action = 1
}

/// <summary>
/// A reference from a contract (or one of its milestones or obligations) to a project document or an action - the contract's papers and the
/// evidence that a milestone was reached or an obligation met. This module never references Documents or Actions (they do not know about each
/// other), so the target is only a Guid plus the title it had when it was linked; the link is not kept in step with the target's life.
/// </summary>
public sealed class ContractLink : AuditableEntity<Guid>
{
    private ContractLink() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public ContractLink(
        Guid id, Guid tenantId, Guid contractId, ContractLinkSubject subjectType, Guid subjectId,
        ContractLinkKind kind, Guid targetId, string title, string? note) : base(id)
    {
        TenantId = tenantId;
        ContractId = contractId;
        SubjectType = subjectType;
        SubjectId = subjectId;
        Kind = kind;
        TargetId = targetId;
        Title = title.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid TenantId { get; private set; }
    public Guid ContractId { get; private set; }
    public ContractLinkSubject SubjectType { get; private set; }

    /// <summary>The contract's own id for a link on the contract; otherwise the milestone's or obligation's.</summary>
    public Guid SubjectId { get; private set; }

    public ContractLinkKind Kind { get; private set; }
    public Guid TargetId { get; private set; }

    /// <summary>The name of the target when it was linked.</summary>
    public string Title { get; private set; }

    public string? Note { get; private set; }

    public void SetNote(string? note) => Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
