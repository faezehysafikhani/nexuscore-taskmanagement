using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Domain;

namespace NexusCore.Infrastructure.Persistence;

public class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserContext currentUserContext;

    public AuditingInterceptor(
        ICurrentUserContext currentUserContext)
    {
        this.currentUserContext = currentUserContext;
    }


    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;

        if (context == null)
            return base.SavingChangesAsync(eventData, result, cancellationToken);


        var now = DateTimeOffset.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditableEntity<Guid> entity)
            {
                if (entry.State == EntityState.Added)
                {
                    entity.CreatedAtUtc = now;
                    entity.CreatedByUserId = currentUserContext.UserId;
                }

                if (entry.State == EntityState.Modified)
                {
                    entity.ModifiedAtUtc = now;
                    entity.ModifiedByUserId = currentUserContext.UserId;
                }
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}