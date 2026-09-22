using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Messaging;

public interface INotificationChannelService
{
    /// <summary>Settings of the caller's tenant (defaults when nothing was saved yet).</summary>
    Task<Result<NotificationChannelSettingsDto>> GetAsync(CancellationToken cancellationToken);

    Task<Result<NotificationChannelSettingsDto>> SaveAsync(NotificationChannelSettingsDto settings, CancellationToken cancellationToken);

    Task<Result<ChannelTestResultDto>> TestSmsAsync(TestSmsRequest request, CancellationToken cancellationToken);
}

/// <summary>Decrypted channel settings of a tenant, for the senders. Never exposed over HTTP.</summary>
public interface INotificationChannelSettingsReader
{
    Task<NotificationChannelSettingsDto> ReadAsync(Guid tenantId, CancellationToken cancellationToken);
}
