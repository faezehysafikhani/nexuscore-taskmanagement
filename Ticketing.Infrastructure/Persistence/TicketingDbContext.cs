using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;
using System.Reflection.Emit;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Entities;

namespace Ticketing.Infrastructure.Persistence;

public class TicketingDbContext : DbContext, ITicketingDbContext
{
    public TicketingDbContext(DbContextOptions<TicketingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>()
            .HasIndex(x => x.Number)
            .IsUnique();

        base.OnModelCreating(modelBuilder);
    }
}