using Events.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notifications.Application.Abstractions;

namespace Events.Infrastructure.Services;

public class EventReminderBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventReminderBackgroundService> _logger;

    public EventReminderBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<EventReminderBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Event Reminder Background Service is starting.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckUpcomingEventsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing event reminders.");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {

        }
    }

    private async Task CheckUpcomingEventsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IEventsDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = DateTime.UtcNow;

        var upcomingEvents = await db.Events
            .Where(e => !e.IsCompleted && !e.ReminderSent && e.ReminderMinutesBefore.HasValue && e.ReminderMinutesBefore > 0)
            .ToListAsync(stoppingToken);

        foreach (var ev in upcomingEvents)
        {
            var reminderThreshold = ev.StartAtUtc.AddMinutes(-ev.ReminderMinutesBefore!.Value);
            if (now >= reminderThreshold && now < ev.StartAtUtc.AddHours(1))
            {
                ev.MarkReminderSent();

                var title = $"یادآوری رویداد: {ev.Title}";
                var message = $"رویداد شما با عنوان '{ev.Title}' در تاریخ {ev.StartAtUtc:yyyy-MM-dd HH:mm} UTC آغاز خواهد شد.";

                await notificationService.NotifyAsync(
                    ev.UserId,
                    title,
                    message,
                    "Warning",
                    stoppingToken);

                _logger.LogInformation("Generated reminder notification for event {EventId} for user {UserId}", ev.Id, ev.UserId);
            }
        }

        await db.SaveChangesAsync(stoppingToken);
    }
}
