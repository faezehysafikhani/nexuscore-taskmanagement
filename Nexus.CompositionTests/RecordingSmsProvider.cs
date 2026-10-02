using System.Collections.Concurrent;
using NexusCore.Application.Messaging;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

/// <summary>An SMS gateway ("test") that records what it was asked to send; never a real gateway.</summary>
public sealed class RecordingSmsProvider : ISmsProvider
{
    public ConcurrentQueue<(string Phone, string Text)> Queue { get; } = new();
    public IReadOnlyList<(string Phone, string Text)> Sent => Queue.ToList();
    public HashSet<string> FailFor { get; } = [];
    public string Key => "test";
    public string DisplayName => "Test";
    public string DefaultBaseUrl => "https://sms.invalid";

    public IReadOnlyList<(string Phone, string Text)> SentTo(string phone) => Queue.Where(m => m.Phone == phone).ToList();

    public Task<Result<string>> SendAsync(SmsProviderSettings settings, string phoneNumber, string text, CancellationToken cancellationToken)
    {
        if (FailFor.Contains(phoneNumber))
        {
            return Task.FromResult(Result.Failure<string>(Error.Validation("سرویس‌دهنده پیامک را نپذیرفت.")));
        }

        Queue.Enqueue((phoneNumber, text));
        return Task.FromResult(Result.Success(Guid.NewGuid().ToString()));
    }
}
