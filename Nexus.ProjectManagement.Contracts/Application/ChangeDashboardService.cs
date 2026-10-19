using System.Text.Json.Serialization;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Application.Persian;
using Nexus.ProjectManagement.Contracts.Domain;
using Nexus.ProjectManagement.Core.Application;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Contracts.Application;

/// <summary>Why an addendum counts as a change made without approval. Sent as its name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UnapprovedChangeReason>))]
public enum UnapprovedChangeReason
{
    /// <summary>The addendum names no change request.</summary>
    NoChangeRequest,

    /// <summary>It names one that is not approved (any more): rejected, cancelled, or not decided yet.</summary>
    ChangeRequestNotApproved
}

public sealed record PendingChangeDto(
    Guid Id, string Code, string Title, DateTimeOffset? SubmittedAtUtc, int WaitingDays, MoneyDto CostImpact, int ScheduleImpactDays);

public sealed record UnapprovedAddendumDto(
    Guid AddendumId, Guid ContractId, string ContractTitle, string ContractNumberFa, int Number, string Title,
    MoneyDto AmountChange, int ExtensionDays, ApprovalStatus ApprovalStatus, Guid? ChangeRequestId, UnapprovedChangeReason Reason);

/// <summary>
/// One project's changes. The approved impact counts approved and implemented change requests; against the project's budget and planned
/// duration it is given as a percentage (null when the project has none). Addenda that are approved or awaiting approval but do not carry
/// out an approved change request are listed as changes made without approval - a warning, nothing is refused.
/// </summary>
public sealed record ChangeDashboardDto(
    Guid ProjectId, int Total, int Drafts, int Pending, int Approved, int Rejected, int Implemented, int Cancelled,
    MoneyDto ApprovedCostImpact, int ApprovedScheduleImpactDays,
    MoneyDto? Budget, decimal? CostImpactPercent, int? PlannedDurationDays, decimal? ScheduleImpactPercent,
    string? PlannedEndDateFa, string? ImpactedEndDateFa,
    IReadOnlyList<PendingChangeDto> PendingChanges,
    IReadOnlyList<UnapprovedAddendumDto> UnapprovedAddenda, MoneyDto UnapprovedAmount, int UnapprovedExtensionDays);

public interface IChangeDashboardService
{
    Task<Result<ChangeDashboardDto>> GetAsync(Guid projectId, CancellationToken cancellationToken);
}

public sealed class ChangeDashboardService(
    IChangeRequestRepository changes,
    IContractRepository contracts,
    IProjectRepository? projects = null,
    TimeProvider? clock = null) : IChangeDashboardService
{
    public async Task<Result<ChangeDashboardDto>> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var items = await changes.ListAsync(projectId, cancellationToken);
        var byId = items.ToDictionary(c => c.Id);
        int Count(ChangeRequestStatus status) => items.Count(c => c.Status == status);

        var decided = items.Where(c => c.Status is ChangeRequestStatus.Approved or ChangeRequestStatus.Implemented).ToList();
        var cost = decided.Sum(c => c.CostImpact);
        var days = decided.Sum(c => c.ScheduleImpactDays);

        // The baseline: the project's budget and planned dates, when it has them.
        var project = projects is null ? null : await projects.GetByIdAsync(projectId, cancellationToken);
        var budget = project?.Cost is > 0 ? project.Cost : null;
        int? duration = project is { StartDate: { } start, EndDate: { } end } && end > start ? end.DayNumber - start.DayNumber : null;

        var pending = items
            .Where(c => c.Status == ChangeRequestStatus.PendingApproval)
            .OrderBy(c => c.SubmittedAtUtc)
            .Select(c => new PendingChangeDto(
                c.Id, Code(c), c.Title, c.SubmittedAtUtc,
                c.SubmittedAtUtc is { } at ? Math.Max(0, (int)(now - at).TotalDays) : 0,
                ContractMapper.Money(c.CostImpact), c.ScheduleImpactDays))
            .ToList();

        // Addenda that took effect, or are about to, without an approved change request behind them.
        var projectContracts = await contracts.ListContractsAsync(projectId, cancellationToken);
        var contractById = projectContracts.ToDictionary(c => c.Id);
        var addenda = await contracts.ListAddendaAsync(projectContracts.Select(c => c.Id).ToList(), cancellationToken);
        var unapproved = new List<UnapprovedAddendumDto>();
        foreach (var addendum in addenda
                     .Where(a => a.ApprovalStatus is ApprovalStatus.Approved or ApprovalStatus.PendingApproval)
                     .OrderBy(a => contractById[a.ContractId].ContractNumber, StringComparer.Ordinal).ThenBy(a => a.Number))
        {
            UnapprovedChangeReason? reason = addendum.ChangeRequestId is not { } linked
                ? UnapprovedChangeReason.NoChangeRequest
                : byId.TryGetValue(linked, out var change) && change.Status is ChangeRequestStatus.Approved or ChangeRequestStatus.Implemented
                    ? null
                    : UnapprovedChangeReason.ChangeRequestNotApproved;
            if (reason is null)
            {
                continue;
            }

            var contract = contractById[addendum.ContractId];
            unapproved.Add(new UnapprovedAddendumDto(
                addendum.Id, contract.Id, contract.Title, PersianNumbers.ToPersianDigits(contract.ContractNumber), addendum.Number, addendum.Title,
                ContractMapper.Money(addendum.AmountChange), addendum.ExtensionDays, addendum.ApprovalStatus, addendum.ChangeRequestId, reason.Value));
        }

        return Result.Success(new ChangeDashboardDto(
            projectId, items.Count, Count(ChangeRequestStatus.Draft), Count(ChangeRequestStatus.PendingApproval), Count(ChangeRequestStatus.Approved),
            Count(ChangeRequestStatus.Rejected), Count(ChangeRequestStatus.Implemented), Count(ChangeRequestStatus.Cancelled),
            ContractMapper.Money(cost), days,
            budget is null ? null : ContractMapper.Money(budget.Value),
            budget is null ? null : Math.Round(cost * 100m / budget.Value, 2, MidpointRounding.AwayFromZero),
            duration,
            duration is null ? null : Math.Round(days * 100m / duration.Value, 2, MidpointRounding.AwayFromZero),
            PersianDates.Format(project?.EndDate),
            project?.EndDate is { } plannedEnd ? PersianDates.Format(plannedEnd.AddDays(days)) : null,
            pending, unapproved,
            ContractMapper.Money(unapproved.Sum(a => a.AmountChange.Rials)), unapproved.Sum(a => a.ExtensionDays)));
    }

    private static string Code(ChangeRequest c) => $"CR-{c.Number:000}";
}
