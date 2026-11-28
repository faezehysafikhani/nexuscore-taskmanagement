using Microsoft.EntityFrameworkCore;
using Nexus.Workflow.Application;
using Nexus.Workflow.Domain;

namespace Nexus.Workflow.Infrastructure;

public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options)
    : DbContext(options), IWorkflowUnitOfWork
{
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowDelegation> WorkflowDelegations => Set<WorkflowDelegation>();

    /// <summary>A concurrent change (two decisions on one approval) is reported as the application's own exception.</summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new WorkflowConcurrencyException(exception);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkflowDbContext).Assembly);
    }
}
