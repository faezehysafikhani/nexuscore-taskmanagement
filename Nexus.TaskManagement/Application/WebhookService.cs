using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

public sealed class WebhookOptions
{
    public const string SectionName = "TaskManagement:Webhooks";

    /// <summary>Plain http is refused unless a host (a development one) allows it.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>Addresses inside the network (loopback, private, link-local) are refused unless a host allows them.</summary>
    public bool AllowPrivateAddresses { get; set; }

    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>The events a webhook can ask for.</summary>
public static class WebhookEvents
{
    public const string TaskCreated = "task.created";
    public const string TaskStatusChanged = "task.status_changed";
    public const string TaskCompleted = "task.completed";
    public const string TaskArchived = "task.archived";
    public const string ReviewDecided = "task.review_decided";
    public const string Ping = "ping";

    public static IReadOnlyList<string> All { get; } = [TaskCreated, TaskStatusChanged, TaskCompleted, TaskArchived, ReviewDecided];
}

/// <summary>Signs what is sent so the receiver knows it came from here: HMAC-SHA256 of the body with the webhook's secret.</summary>
public static class WebhookSigner
{
    public static string Sign(string secret, string body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public static string NewSecret() => "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}

/// <summary>Keeps webhooks from being pointed at the organization's own network (SSRF): the address, not only the name, must be public.</summary>
public static class WebhookAddressRules
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (address.GetAddressBytes()[0] & 0xFE) == 0xFC);
        }

        var b = address.GetAddressBytes();
        return !(b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] is >= 64 and <= 127)
            || b[0] == 0 || b[0] >= 224);
    }
}

public sealed class WebhookService(
    IWebhookRepository repository,
    IWebhookPublisher publisher,
    ITaskManagementUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IOptions<WebhookOptions> options) : IWebhookService
{
    public const int MaxPerTenant = 20;

    private static WebhookDto ToDto(WebhookSubscription w) => new(
        w.Id, w.Name, w.Url, w.Events == "*" ? ["*"] : w.Events.Split(',', StringSplitOptions.RemoveEmptyEntries), w.IsActive, w.ConsecutiveFailures, w.LastDeliveredAtUtc);

    private async Task<Error?> CheckAsync(SaveWebhookRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return Error.Validation("The address must be a full URL without a user name or password.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(options.Value.AllowHttp && uri.Scheme == Uri.UriSchemeHttp))
        {
            return Error.Validation("The address must use https.");
        }

        if (!options.Value.AllowPrivateAddresses)
        {
            try
            {
                var addresses = IPAddress.TryParse(uri.IdnHost, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(uri.IdnHost, cancellationToken);
                if (addresses.Length == 0 || addresses.Any(a => !WebhookAddressRules.IsPublic(a)))
                {
                    return Error.Validation("This address is inside a private network and is not allowed.");
                }
            }
            catch (SocketException)
            {
                return Error.Validation("The address could not be resolved.");
            }
        }

        var unknown = (request.Events ?? []).Where(e => e != "*" && !WebhookEvents.All.Contains(e)).ToList();
        return unknown.Count > 0 ? Error.Validation($"Unknown event: {unknown[0]}.") : null;
    }

    private static string Events(SaveWebhookRequest request) =>
        request.Events is null or { Count: 0 } || request.Events.Contains("*") ? "*" : string.Join(',', request.Events.Distinct());

    public async Task<Result<IReadOnlyList<WebhookDto>>> ListAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is not { } tenantId
            ? Result.Failure<IReadOnlyList<WebhookDto>>(Error.Unauthorized())
            : Result.Success<IReadOnlyList<WebhookDto>>((await repository.ListAsync(tenantId, cancellationToken)).Select(ToDto).ToList());

    public async Task<Result<WebhookWithSecretDto>> CreateAsync(SaveWebhookRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<WebhookWithSecretDto>(Error.Unauthorized());
        }

        if (await CheckAsync(request, cancellationToken) is { } invalid)
        {
            return Result.Failure<WebhookWithSecretDto>(invalid);
        }

        if ((await repository.ListAsync(tenantId, cancellationToken)).Count >= MaxPerTenant)
        {
            return Result.Failure<WebhookWithSecretDto>(Error.Validation($"At most {MaxPerTenant} webhooks."));
        }

        var secret = WebhookSigner.NewSecret();
        var webhook = new WebhookSubscription(Guid.NewGuid(), tenantId, request.Name, request.Url, secret, Events(request));
        await repository.AddAsync(webhook, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new WebhookWithSecretDto(ToDto(webhook), secret));
    }

    public async Task<Result<WebhookDto>> UpdateAsync(Guid id, SaveWebhookRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<WebhookDto>(Error.Unauthorized());
        }

        var webhook = await repository.GetAsync(tenantId, id, cancellationToken);
        if (webhook is null)
        {
            return Result.Failure<WebhookDto>(Error.NotFound("Webhook not found."));
        }

        if (await CheckAsync(request, cancellationToken) is { } invalid)
        {
            return Result.Failure<WebhookDto>(invalid);
        }

        webhook.Update(request.Name, request.Url, Events(request), request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(webhook));
    }

    public async Task<Result<WebhookWithSecretDto>> RotateSecretAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<WebhookWithSecretDto>(Error.Unauthorized());
        }

        var webhook = await repository.GetAsync(tenantId, id, cancellationToken);
        if (webhook is null)
        {
            return Result.Failure<WebhookWithSecretDto>(Error.NotFound("Webhook not found."));
        }

        var secret = WebhookSigner.NewSecret();
        webhook.RotateSecret(secret);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new WebhookWithSecretDto(ToDto(webhook), secret));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var webhook = await repository.GetAsync(tenantId, id, cancellationToken);
        if (webhook is null)
        {
            return Result.Failure(Error.NotFound("Webhook not found."));
        }

        await repository.RemoveDeliveriesOfAsync(id, cancellationToken);
        repository.Remove(webhook);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<WebhookDeliveryDto>>> ListDeliveriesAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<WebhookDeliveryDto>>(Error.Unauthorized());
        }

        if (await repository.GetAsync(tenantId, id, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<WebhookDeliveryDto>>(Error.NotFound("Webhook not found."));
        }

        return Result.Success<IReadOnlyList<WebhookDeliveryDto>>((await repository.ListDeliveriesAsync(tenantId, id, 50, cancellationToken))
            .Select(d => new WebhookDeliveryDto(d.Id, d.EventType, d.Attempts, d.DeliveredAtUtc is not null, d.GaveUpAtUtc is not null, d.LastStatusCode, d.LastError, d.CreatedAtUtc, d.DeliveredAtUtc)).ToList());
    }

    public async Task<Result> PingAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var webhook = await repository.GetAsync(tenantId, id, cancellationToken);
        if (webhook is null)
        {
            return Result.Failure(Error.NotFound("Webhook not found."));
        }

        await repository.AddDeliveriesAsync([new WebhookDelivery(Guid.NewGuid(), tenantId, id, WebhookEvents.Ping,
            WebhookPublisher.Envelope(tenantId, WebhookEvents.Ping, new { message = "Hello from the task manager." }), DateTimeOffset.UtcNow)], cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class WebhookPublisher(IWebhookRepository repository, ITaskManagementUnitOfWork unitOfWork, ILogger<WebhookPublisher> logger) : IWebhookPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static string Envelope(Guid tenantId, string eventType, object data) =>
        JsonSerializer.Serialize(new { id = Guid.NewGuid(), @event = eventType, tenantId, createdAtUtc = DateTimeOffset.UtcNow, data }, Json);

    public async Task PublishAsync(Guid tenantId, string eventType, object data, CancellationToken cancellationToken)
    {
        try
        {
            var wanting = (await repository.ListActiveAsync(tenantId, cancellationToken)).Where(w => w.Wants(eventType)).ToList();
            if (wanting.Count == 0)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            await repository.AddDeliveriesAsync(
                wanting.Select(w => new WebhookDelivery(Guid.NewGuid(), tenantId, w.Id, eventType, Envelope(tenantId, eventType, data), now)), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not queue the webhook event {Event}.", eventType);
        }
    }
}
