using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Domain;
using Notifications.Application.Abstractions;

namespace Nexus.Bundles;

/// <summary>
/// In-app notifications about approvals (M6): the approver of the step now waiting is told an item waits for them, and
/// the submitter is told when it is approved or rejected (with the reason). Only for the subject types listed in
/// <see cref="Subjects"/> (project charters). A failed notification is logged and never fails the approval itself.
/// </summary>
public sealed class ApprovalNotifications(IServiceScopeFactory scopeFactory, ILogger<ApprovalNotifications> logger)
    : IDomainEventHandler<ApprovalStepAssigned>, IDomainEventHandler<ApprovalGranted>, IDomainEventHandler<ApprovalRejected>
{
    public const string Category = "approval";

    /// <summary>The subject types notified, with how they are named in a message.</summary>
    public static IReadOnlyDictionary<string, string> Subjects { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Project"] = "منشور پروژه"
    };

    public async Task HandleAsync(ApprovalStepAssigned domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.ApproverUserId is not { } approver || !Subjects.TryGetValue(domainEvent.SubjectType, out var kind))
        {
            return;
        }

        var name = await NameAsync(domainEvent.SubjectType, domainEvent.SubjectId, kind, cancellationToken);
        await SendAsync(new NotificationRequest(approver, "درخواست تأیید",
            $"{name} برای تأیید شما (مرحله‌ی «{domainEvent.StepName}») ارسال شد.", "Info", domainEvent.TenantId, Category,
            domainEvent.SubjectId, domainEvent.SubmittedByUserId), cancellationToken);
    }

    public async Task HandleAsync(ApprovalGranted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubmittedByUserId is not { } submitter || !Subjects.TryGetValue(domainEvent.SubjectType, out var kind))
        {
            return;
        }

        var name = await NameAsync(domainEvent.SubjectType, domainEvent.SubjectId, kind, cancellationToken);
        await SendAsync(new NotificationRequest(submitter, "تأیید شد", $"{name} تأیید شد.", "Success", domainEvent.TenantId, Category,
            domainEvent.SubjectId, domainEvent.DecidedByUserId), cancellationToken);
    }

    public async Task HandleAsync(ApprovalRejected domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.SubmittedByUserId is not { } submitter || !Subjects.TryGetValue(domainEvent.SubjectType, out var kind))
        {
            return;
        }

        var name = await NameAsync(domainEvent.SubjectType, domainEvent.SubjectId, kind, cancellationToken);
        var reason = string.IsNullOrWhiteSpace(domainEvent.Comment) ? "" : $" دلیل: {domainEvent.Comment.Trim()}";
        await SendAsync(new NotificationRequest(submitter, "رد شد", $"{name} رد شد.{reason}", "Warning", domainEvent.TenantId, Category,
            domainEvent.SubjectId, domainEvent.DecidedByUserId), cancellationToken);
    }

    private async Task<string> NameAsync(string subjectType, Guid subjectId, string kind, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            foreach (var describer in scope.ServiceProvider.GetServices<IApprovalSubjectDescriber>())
            {
                if (await describer.DescribeAsync(subjectType, subjectId, cancellationToken) is { Length: > 0 } name)
                {
                    return $"{kind} «{name}»";
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The name of approval subject {SubjectType} {SubjectId} could not be read.", subjectType, subjectId);
        }

        return kind;
    }

    private async Task SendAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Approval notification for user {UserId} about {ReferenceId} was not stored.", request.UserId, request.ReferenceId);
        }
    }
}
