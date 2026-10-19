using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Meetings.Application;
using Nexus.ProjectManagement.Meetings.Domain;

namespace Nexus.ProjectManagement.Meetings.Infrastructure;

public sealed class MeetingDbContext(DbContextOptions<MeetingDbContext> options)
    : DbContext(options), IMeetingUnitOfWork
{
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<MeetingParticipant> MeetingParticipants => Set<MeetingParticipant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MeetingDbContext).Assembly);
    }
}
