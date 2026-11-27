using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Messaging;

namespace Nexus.TaskManagement.Infrastructure;

/// <summary>
/// Sends the scheduled digests (report distribution): each due subscription is advanced first - so a failing send costs one digest,
/// never a loop - then the person's own tasks are counted and the digest goes by SMS (their mobile number) and/or email.
/// Opt-in with the module's other jobs (TaskManagement:Scheduler:Enabled).
/// </summary>
public sealed class ReportDistributionService(
    IServiceProvider serviceProvider,
    IOptions<RepetitiveTaskSchedulerOptions> options,
    TimeProvider clock,
    ILogger<ReportDistributionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(clock.GetUtcNow(), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Report distribution pass failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass; returns how many digests were handled (public so a test can run it).</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        var due = await db.ReportSubscriptions.Where(s => s.IsActive && s.NextRunAtUtc <= now).OrderBy(s => s.NextRunAtUtc).Take(200).ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        // Advance before sending.
        foreach (var s in due)
        {
            s.MarkSent(now);
        }

        await db.SaveChangesAsync(cancellationToken);

        var digests = scope.ServiceProvider.GetRequiredService<ITaskDigestSource>();
        var users = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsSender>();
        var email = scope.ServiceProvider.GetService<IEmailSender>();
        var today = DateOnly.FromDateTime(now.ToOffset(ReportSubscription.IranOffset).DateTime);
        foreach (var s in due)
        {
            try
            {
                var contact = (await users.GetUsersAsync([s.UserId], cancellationToken)).FirstOrDefault();
                if (contact is null || !contact.IsActive)
                {
                    continue;
                }

                var digest = await digests.BuildAsync(s.TenantId, s.UserId, today, cancellationToken);
                if (s.BySms && !string.IsNullOrWhiteSpace(contact.PhoneNumber))
                {
                    var sent = await sms.SendAsync(s.TenantId, contact.PhoneNumber, TaskDigestText.Sms(today, digest), cancellationToken);
                    if (sent.IsFailure)
                    {
                        logger.LogWarning("Digest SMS for {Subscription} was not sent ({Code}).", s.Id, sent.Error.Code);
                    }
                }

                if (s.ByEmail && email is { IsConfigured: true } && !string.IsNullOrWhiteSpace(contact.Email))
                {
                    var sent = await email.SendAsync(new EmailMessage(contact.Email, TaskDigestText.Subject(today), TaskDigestText.Email(today, digest)), cancellationToken);
                    if (sent.IsFailure)
                    {
                        logger.LogWarning("Digest email for {Subscription} was not sent ({Code}).", s.Id, sent.Error.Code);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Digest {Subscription} could not be built or sent.", s.Id);
            }
        }

        return due.Count;
    }
}
