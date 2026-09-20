using NexusCore.SharedKernel.Interfaces;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// The module's unit of work. Implemented by TaskManagementDbContext, so the service layer
/// commits without referencing EF Core - and so creating a project together with its first
/// subtasks, or a task together with its recurrence schedule, commits as one transaction.
/// </summary>
public interface ITaskManagementUnitOfWork : IUnitOfWork
{
}
