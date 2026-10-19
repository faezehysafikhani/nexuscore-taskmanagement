using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Meetings.Application;
using Nexus.ProjectManagement.Meetings.Domain;

namespace Nexus.ProjectManagement.Meetings.Infrastructure;

public sealed class MeetingRepository(MeetingDbContext dbContext) : IMeetingRepository
{
    public Task<Meeting?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Meetings.Include(m => m.Participants).SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Meeting>> ListByProjectAsync(Guid projectId, MeetingStatus? status, CancellationToken cancellationToken)
    {
        var query = dbContext.Meetings.Include(m => m.Participants).Where(m => m.ProjectId == projectId);
        if (status is { } wanted)
        {
            query = query.Where(m => m.Status == wanted);
        }

        return await query.OrderByDescending(m => m.MeetingDate).ThenByDescending(m => m.StartTime).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Meeting>> ListForProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken) =>
        await dbContext.Meetings.Include(m => m.Participants)
            .Where(m => projectIds.Contains(m.ProjectId))
            .OrderByDescending(m => m.MeetingDate).ThenByDescending(m => m.StartTime)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Meeting meeting, CancellationToken cancellationToken)
    {
        await dbContext.Meetings.AddAsync(meeting, cancellationToken);
    }

    public Task RemoveAsync(Meeting meeting, CancellationToken cancellationToken)
    {
        dbContext.Meetings.Remove(meeting);
        return Task.CompletedTask;
    }
}
