using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;

namespace Nexus.TaskManagement.Infrastructure;

/// <summary>
/// Sends the queued webhook events (the outbox): each row goes to its subscription, signed, with a short timeout; a failure
/// is retried with a growing delay and given up on after <see cref="WebhookDelivery.MaxAttempts"/> tries. A row is held
/// (leased) while it is being sent so another server does not send it too. At-least-once: the receiver can tell a repeat
/// by the delivery id in the X-TM-Delivery header.
/// </summary>
public sealed class WebhookDeliveryService(
    IServiceScopeFactory scopes,
    IOptions<WebhookOptions> options,
    TimeProvider clock,
    ILogger<WebhookDeliveryService> logger)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>The client that sends: it connects only to a public address - checked at connect time, so a name that later points inside cannot get through.</summary>
    public static HttpClient CreateClient(WebhookOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                var allowed = options.AllowPrivateAddresses ? addresses : addresses.Where(WebhookAddressRules.IsPublic).ToArray();
                if (allowed.Length == 0)
                {
                    throw new HttpRequestException("The address is not allowed.");
                }

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds) + 5) };
    }

    /// <summary>One pass over the rows that are due; returns how many were tried. <paramref name="http"/> lets a test send nowhere real.</summary>
    public async Task<int> DeliverDueAsync(HttpClient http, int batch, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        List<Guid> ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
            var due = await db.WebhookDeliveries.Where(d => d.DeliveredAtUtc == null && d.GaveUpAtUtc == null && d.NextAttemptAtUtc <= now)
                .OrderBy(d => d.NextAttemptAtUtc).Take(batch).ToListAsync(cancellationToken);
            foreach (var row in due)
            {
                row.Lease(now + Lease);
            }

            await db.SaveChangesAsync(cancellationToken);
            ids = due.Select(d => d.Id).ToList();
        }

        foreach (var id in ids)
        {
            try
            {
                await SendOneAsync(http, id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Webhook delivery {Delivery} failed unexpectedly.", id);
            }
        }

        return ids.Count;
    }

    private async Task SendOneAsync(HttpClient http, Guid id, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        var row = await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (row is null || !row.IsPending)
        {
            return;
        }

        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == row.SubscriptionId, cancellationToken);
        var now = clock.GetUtcNow();
        if (subscription is null || !subscription.IsActive)
        {
            row.GiveUp(subscription is null ? "The webhook was deleted." : "The webhook is switched off.", now);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url) { Content = new StringContent(row.Payload, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("X-TM-Event", row.EventType);
        request.Headers.TryAddWithoutValidation("X-TM-Delivery", row.Id.ToString());
        request.Headers.TryAddWithoutValidation("X-TM-Signature", WebhookSigner.Sign(subscription.Secret, row.Payload));
        request.Headers.TryAddWithoutValidation("User-Agent", "TaskManager-Webhooks/1");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)));
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            var code = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                row.Succeeded(code, now);
                subscription.RecordSuccess(now);
            }
            else
            {
                row.Failed(code, $"The receiver answered {code}.", now);
                subscription.RecordFailure();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            row.Failed(null, ex is HttpRequestException ? ex.Message : "The receiver did not answer in time.", now);
            subscription.RecordFailure();
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Runs <see cref="WebhookDeliveryService"/> on a timer; opt-in like the module's other jobs (TaskManagement:Scheduler:Enabled).</summary>
public sealed class WebhookDeliveryBackgroundService(
    WebhookDeliveryService delivery,
    IOptions<RepetitiveTaskSchedulerOptions> schedulerOptions,
    IOptions<WebhookOptions> webhookOptions,
    ILogger<WebhookDeliveryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!schedulerOptions.Value.Enabled)
        {
            return;
        }

        using var http = WebhookDeliveryService.CreateClient(webhookOptions.Value);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await delivery.DeliverDueAsync(http, 50, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Webhook delivery pass failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
