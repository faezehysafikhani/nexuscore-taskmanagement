using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusCore.SharedKernel.Results;

namespace Nexus.TaskManagement.Application;

/// <summary>
/// SMS settings, read from configuration under TaskManagement:Sms.
///
/// No provider is named here. NexusCore ships no SMS infrastructure and its configuration
/// mentions no gateway, so choosing one would be a guess. <see cref="Provider"/> is a free
/// string that a real adapter can switch on once someone supplies credentials.
///
/// Nothing sensitive belongs in appsettings.json: supply ApiKey through user-secrets,
/// environment variables or a vault.
/// </summary>
public sealed class TaskSmsOptions
{
    public const string SectionName = "TaskManagement:Sms";

    /// <summary>Off unless explicitly switched on, so no deployment sends messages by accident.</summary>
    public bool Enabled { get; set; }

    public string? Provider { get; set; }

    public string? ApiKey { get; set; }

    public string? SenderNumber { get; set; }

    public string? ApiUrl { get; set; }

    /// <summary>True only when the feature is on and the settings a send would need are present.</summary>
    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Provider)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(SenderNumber);
}

/// <summary>
/// The shipped SMS sender: it never contacts a gateway.
///
/// This is deliberate, not a stub left by accident. There is no SMS provider configured
/// anywhere in NexusCore, and sending real messages from a test or development environment
/// would be both wrong and expensive. It logs what it would have sent, which keeps the whole
/// path - job, handler, recipient resolution, message text - exercisable end to end.
///
/// To send for real, implement ITaskSmsSender against your gateway and register it in place
/// of this one; everything upstream stays as it is.
/// </summary>
public sealed class LoggingTaskSmsSender(
    IOptions<TaskSmsOptions> options,
    ILogger<LoggingTaskSmsSender> logger) : ITaskSmsSender
{
    private readonly TaskSmsOptions _options = options.Value;

    public Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(_options.Enabled);

    public Task<Result> SendAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(Result.Failure(Error.Validation("SMS delivery is disabled.")));
        }

        if (!_options.IsConfigured)
        {
            logger.LogWarning(
                "SMS is enabled but not fully configured ({Section}). Nothing was sent.", TaskSmsOptions.SectionName);
            return Task.FromResult(Result.Failure(Error.Validation("SMS settings are incomplete.")));
        }

        // Only the last four digits are logged - a full number in the logs is a privacy leak.
        var masked = phoneNumber.Length <= 4 ? "****" : $"****{phoneNumber[^4..]}";
        logger.LogInformation(
            "SMS would be sent to {MaskedNumber} via {Provider}: {Message}",
            masked, _options.Provider, message);

        return Task.FromResult(Result.Success());
    }
}
