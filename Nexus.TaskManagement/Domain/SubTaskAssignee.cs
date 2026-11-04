using NexusCore.SharedKernel.Domain;
using NexusCore.Domain.Identity;

namespace Nexus.TaskManagement.Domain;

/// <summary>One person responsible for a <see cref="SubTask"/>. A subtask may have several.</summary>
public sealed class SubTaskAssignee : Entity<Guid>
{
    private SubTaskAssignee() : base(Guid.Empty)
    {
    }

    public SubTaskAssignee(Guid id, Guid subTaskId, Guid userId) : base(id)
    {
        SubTaskId = subTaskId;
        UserId = userId;
    }

    public Guid SubTaskId { get; private set; }
    public SubTask? SubTask { get; private set; }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }
}
