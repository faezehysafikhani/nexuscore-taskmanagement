using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Abstractions;

public interface ITicketingDbContext
{
    DbSet<Ticket> Tickets { get; }
    DbSet<TicketComment> TicketComments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}