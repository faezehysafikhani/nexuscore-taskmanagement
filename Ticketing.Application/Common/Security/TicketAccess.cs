using NexusCore.SharedKernel.Interfaces;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Common.Security;

/// <summary>
/// Which tickets a caller may reach, applied inside the query: always their own organization
/// only; within it, the tickets they raised or are assigned to, or all of them with
/// Tickets.Manage. A ticket outside that set answers "not found", whatever id is sent.
/// </summary>
public static class TicketAccess
{
    public static IQueryable<Ticket> VisibleTo(this IQueryable<Ticket> tickets, ICurrentUserContext currentUser)
    {
        var tenantId = currentUser.TenantId;
        var userId = currentUser.UserId;
        var scoped = tickets.Where(ticket => ticket.TenantId == tenantId);
        return currentUser.HasPermission(TicketingPermissions.Manage)
            ? scoped
            : scoped.Where(ticket => ticket.CreatedByUserId == userId || ticket.AssignedToUserId == userId);
    }
}
