using Nexus.ProjectManagement.Contracts.Domain;

namespace Nexus.ProjectManagement.Contracts.Application.Dtos;

/// <summary>Dates are Gregorian; the ...Fa fields are the same dates in the Jalali calendar. IsOverdue is judged today.</summary>
public sealed record ContractMilestoneDto(
    Guid Id, Guid ContractId, string Title, string? Description, DateOnly DueDate, string DueDateFa,
    ContractMilestoneStatus Status, DateOnly? CompletedDate, string? CompletedDateFa, bool IsOverdue, Guid? CreatedByUserId);

public sealed record ContractObligationDto(
    Guid Id, Guid ContractId, string Title, string? Description, ObligationParty Party, DateOnly? DueDate, string? DueDateFa,
    ObligationStatus Status, DateOnly? FulfilledDate, string? FulfilledDateFa, bool IsOverdue, Guid? CreatedByUserId);

/// <summary>CompletedDate is only kept for a Completed milestone (it defaults to today then); any other status clears it.</summary>
public sealed record SaveContractMilestoneRequest(
    string Title, DateOnly DueDate, string? Description = null,
    ContractMilestoneStatus Status = ContractMilestoneStatus.NotStarted, DateOnly? CompletedDate = null);

/// <summary>FulfilledDate is only kept for a Fulfilled obligation (it defaults to today then); any other status clears it.</summary>
public sealed record SaveContractObligationRequest(
    string Title, ObligationParty Party = ObligationParty.Supplier, string? Description = null, DateOnly? DueDate = null,
    ObligationStatus Status = ObligationStatus.Open, DateOnly? FulfilledDate = null);

/// <summary>What is still owed on one contract, for a glance.</summary>
public sealed record ContractCommitmentsSummaryDto(
    int MilestoneCount, int CompletedMilestones, int OverdueMilestones, int ObligationCount, int FulfilledObligations,
    int OverdueObligations, int BreachedObligations);
