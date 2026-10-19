using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Meetings.Domain;

public enum MeetingStatus
{
    Scheduled,
    Held,
    Cancelled
}

public enum AttendanceStatus
{
    Pending,
    Attended,
    Absent,
    Excused
}

public sealed class MeetingParticipant : Entity<Guid>
{
    private MeetingParticipant() : base(Guid.Empty)
    {
    }

    internal MeetingParticipant(Guid id, Guid meetingId, Guid userId, bool isRequired) : base(id)
    {
        MeetingId = meetingId;
        UserId = userId;
        IsRequired = isRequired;
        Attendance = AttendanceStatus.Pending;
    }

    public Guid MeetingId { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsRequired { get; private set; }
    public AttendanceStatus Attendance { get; private set; }

    internal void SetRequired(bool isRequired) => IsRequired = isRequired;
    internal void SetAttendance(AttendanceStatus attendance) => Attendance = attendance;
}

/// <summary>A project meeting: when and where it is, who is invited and - once it has been held - who came and what
/// was decided (the minutes). ProjectId is a bare Guid so the module does not depend on Project Core.</summary>
public sealed class Meeting : AuditableEntity<Guid>
{
    private readonly List<MeetingParticipant> _participants = [];

    private Meeting() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public Meeting(Guid id, Guid tenantId, Guid projectId, string title, DateOnly meetingDate) : base(id)
    {
        TenantId = tenantId;
        ProjectId = projectId;
        Title = title.Trim();
        MeetingDate = meetingDate;
        Status = MeetingStatus.Scheduled;
    }

    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Title { get; private set; }
    public string? Agenda { get; private set; }
    public DateOnly MeetingDate { get; private set; }
    public TimeOnly? StartTime { get; private set; }
    public int? DurationMinutes { get; private set; }
    public string? Location { get; private set; }
    public string? OnlineLink { get; private set; }
    public Guid? OrganizerUserId { get; private set; }
    public MeetingStatus Status { get; private set; }
    public string? Minutes { get; private set; }
    public Guid? MinutesRecordedByUserId { get; private set; }
    public DateTimeOffset? MinutesRecordedAtUtc { get; private set; }
    public IReadOnlyCollection<MeetingParticipant> Participants => _participants.AsReadOnly();

    public void UpdateDetails(
        string title, string? agenda, DateOnly meetingDate, TimeOnly? startTime, int? durationMinutes,
        string? location, string? onlineLink, Guid? organizerUserId)
    {
        Title = title.Trim();
        Agenda = Clean(agenda);
        MeetingDate = meetingDate;
        StartTime = startTime;
        DurationMinutes = durationMinutes;
        Location = Clean(location);
        OnlineLink = Clean(onlineLink);
        OrganizerUserId = organizerUserId;
    }

    public void SetStatus(MeetingStatus status) => Status = status;

    /// <summary>Makes the invited people exactly <paramref name="wanted"/>; someone who stays keeps the attendance recorded for them.</summary>
    public void SetParticipants(IEnumerable<(Guid UserId, bool IsRequired)> wanted)
    {
        var list = wanted.GroupBy(item => item.UserId).Select(group => group.First()).ToList();
        _participants.RemoveAll(existing => list.All(item => item.UserId != existing.UserId));
        foreach (var (userId, isRequired) in list)
        {
            var existing = _participants.SingleOrDefault(p => p.UserId == userId);
            if (existing is null)
            {
                _participants.Add(new MeetingParticipant(Guid.NewGuid(), Id, userId, isRequired));
            }
            else
            {
                existing.SetRequired(isRequired);
            }
        }
    }

    public bool SetAttendance(Guid userId, AttendanceStatus attendance)
    {
        var participant = _participants.SingleOrDefault(p => p.UserId == userId);
        if (participant is null)
        {
            return false;
        }

        participant.SetAttendance(attendance);
        return true;
    }

    public void RecordMinutes(string? minutes, Guid? recordedByUserId, DateTimeOffset recordedAtUtc)
    {
        Minutes = Clean(minutes);
        MinutesRecordedByUserId = Minutes is null ? null : recordedByUserId;
        MinutesRecordedAtUtc = Minutes is null ? null : recordedAtUtc;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
