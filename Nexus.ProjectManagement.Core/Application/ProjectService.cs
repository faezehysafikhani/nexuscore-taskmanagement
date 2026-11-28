using NexusCore.SharedKernel.Interfaces;
using NexusCore.Application.Identity.Interfaces;
using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Application;

public sealed class ProjectService(
    IProjectRepository repository,
    IProjectManagementUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester,
    IEnumerable<IProjectDeletionParticipant>? deletionParticipants = null,
    ProjectOptions? options = null,
    IEnumerable<IProjectSubmissionCheck>? submissionChecks = null,
    IWorkAssignmentScope? assignmentScope = null,
    IUserReachScope? reachScope = null,
    ICurrentUserContext? currentUser = null,
    IApprovalTracker? approvalTracker = null) : IProjectService
{
    /// <summary>The charter's approval: scoped to the project, so a chain set for this project wins over the general one.</summary>
    private static ApprovalSubject SubjectOf(Project project) =>
        new("Project", project.Id, project.TenantId, ScopeType: "Project", ScopeId: project.Id);

    public async Task<Result<PagedResult<ProjectDto>>> ListAsync(ListProjectsRequest request, CancellationToken cancellationToken)
    {
        var normalized = request with
        {
            PageNumber = Math.Max(1, request.PageNumber),
            PageSize = Math.Clamp(request.PageSize, 1, 200)
        };

        var projects = await repository.ListAsync(normalized, cancellationToken);
        return Result.Success(new PagedResult<ProjectDto>(
            projects.Items.Select(ToDto).ToList(),
            projects.PageNumber,
            projects.PageSize,
            projects.TotalCount));
    }

    public async Task<Result<ProjectDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return Result.Failure<ProjectDto>(Error.NotFound("Project not found."));
        }

        // A project left "pending" while its approval was already decided (see SyncApprovalAsync) is put right on read.
        if (await SyncWithApprovalAsync(project, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(ToDto(project));
    }

    /// <summary>
    /// Brings the project's approval state in line with its approval in the backend: a decision whose follow-up failed
    /// (the project stayed "pending"), or a submission that never got an approval (withdrawn or lost) is repaired.
    /// </summary>
    public async Task<Result<ProjectDto>> SyncApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return Result.Failure<ProjectDto>(Error.NotFound("Project not found."));
        }

        if (await SyncWithApprovalAsync(project, cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(ToDto(project));
    }

    private async Task<bool> SyncWithApprovalAsync(Project project, CancellationToken cancellationToken)
    {
        if (project.ApprovalStatus != ApprovalStatus.PendingApproval || approvalTracker is null)
        {
            return false;
        }

        var latest = await approvalTracker.GetLatestAsync(SubjectOf(project), cancellationToken);
        switch (latest?.State)
        {
            case ApprovalRunState.InProgress:
                return false;
            case ApprovalRunState.Approved:
                project.Approve();
                return true;
            case ApprovalRunState.Rejected:
                project.Reject();
                return true;
            default:
                // Withdrawn, or no approval at all: nothing is deciding it - it can be sent again.
                project.ResetApproval();
                return true;
        }
    }

    public async Task<Result> CheckAsync(CreateProjectRequest request, Guid? existingProjectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure(Error.Validation("Name is required."));
        }

        var charterError = CheckCharter(request.Name, request.ManagerUserId, request.OrganizationUnitId, request.StartDate, request.EndDate,
            request.Goal, request.Requirements, request.Constraints, request.Assumptions, request.Description);
        if (charterError is not null)
        {
            return Result.Failure(charterError);
        }

        if (request.StartDate is { } start && request.EndDate is { } end && end < start)
        {
            return Result.Failure(Error.Validation("End date must be on or after the start date."));
        }

        if (options?.RequireUniqueName == true && await repository.NameExistsAsync(request.TenantId, request.Name, existingProjectId, cancellationToken))
        {
            return Result.Failure(new Error("project.name_exists", "A project with this name already exists."));
        }

        if (!string.IsNullOrWhiteSpace(request.Code) && await repository.CodeExistsAsync(request.TenantId, request.Code.Trim(), existingProjectId, cancellationToken))
        {
            return Result.Failure(Error.Conflict("A project with this code already exists."));
        }

        return Result.Success();
    }

    public async Task<Result<ProjectDto>> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<ProjectDto>(Error.Validation("Name is required."));
        }

        var charterError = CheckCharter(request.Name, request.ManagerUserId, request.OrganizationUnitId, request.StartDate, request.EndDate,
            request.Goal, request.Requirements, request.Constraints, request.Assumptions, request.Description);
        if (charterError is not null)
        {
            return Result.Failure<ProjectDto>(charterError);
        }

        if (options?.RequireUniqueName == true && await repository.NameExistsAsync(request.TenantId, request.Name, null, cancellationToken))
        {
            return Result.Failure<ProjectDto>(new Error("project.name_exists", "A project with this name already exists."));
        }

        var code = string.IsNullOrWhiteSpace(request.Code)
            ? await repository.NextCodeAsync(request.TenantId, cancellationToken) ?? $"P{Guid.NewGuid():N}"[..9]
            : request.Code.Trim();

        if (await repository.CodeExistsAsync(request.TenantId, code, null, cancellationToken))
        {
            return Result.Failure<ProjectDto>(Error.Conflict("A project with this code already exists."));
        }

        if (await WorkAssignmentCheck.CheckAsync(assignmentScope, reachScope, currentUser, request.TenantId, request.ManagerUserId, null,
                "مدیر پروژه", cancellationToken) is { } managerError)
        {
            return Result.Failure<ProjectDto>(managerError);
        }

        var project = new Project(Guid.NewGuid(), request.TenantId, request.Name, code, request.Type, request.ManagerUserId, request.OwnerUserId);
        project.UpdateDetails(
            request.Name, code, request.ManagerUserId, request.OwnerUserId,
            request.OrganizationUnitId, request.WorkCalendarId, request.StartDate, request.EndDate, request.Cost,
            request.Goal, request.Requirements, request.Constraints, request.Assumptions, request.Description, request.Charter);

        await repository.AddAsync(project, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(project));
    }

    public async Task<Result<ProjectDto>> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return Result.Failure<ProjectDto>(Error.NotFound("Project not found."));
        }

        // While the charter is with the approvers it does not change under them.
        await SyncWithApprovalAsync(project, cancellationToken);
        if (project.ApprovalStatus == ApprovalStatus.PendingApproval)
        {
            return Result.Failure<ProjectDto>(ProjectErrors.PendingApproval);
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<ProjectDto>(Error.Validation("Name and code are required."));
        }

        var charterError = CheckCharter(request.Name, request.ManagerUserId, request.OrganizationUnitId, request.StartDate, request.EndDate,
            request.Goal, request.Requirements, request.Constraints, request.Assumptions, request.Description);
        if (charterError is not null)
        {
            return Result.Failure<ProjectDto>(charterError);
        }

        if (options?.RequireUniqueName == true && await repository.NameExistsAsync(project.TenantId, request.Name, id, cancellationToken))
        {
            return Result.Failure<ProjectDto>(new Error("project.name_exists", "A project with this name already exists."));
        }

        if (await repository.CodeExistsAsync(project.TenantId, request.Code, id, cancellationToken))
        {
            return Result.Failure<ProjectDto>(Error.Conflict("A project with this code already exists."));
        }

        if (await WorkAssignmentCheck.CheckAsync(assignmentScope, reachScope, currentUser, project.TenantId, request.ManagerUserId, project.ManagerUserId,
                "مدیر پروژه", cancellationToken) is { } managerError)
        {
            return Result.Failure<ProjectDto>(managerError);
        }

        // «تصمیم ۲»: after approval, changing the manager, dates, cost, goal or requirements sends the charter for approval
        // again (the project keeps working meanwhile); other fields change freely.
        var needsReapproval = project.ApprovalStatus == ApprovalStatus.Approved
            && project.ChangesApprovedCharter(request.ManagerUserId, request.StartDate, request.EndDate, request.Cost, request.Goal, request.Requirements);

        project.UpdateDetails(
            request.Name, request.Code, request.ManagerUserId, request.OwnerUserId,
            request.OrganizationUnitId, request.WorkCalendarId, request.StartDate, request.EndDate, request.Cost,
            request.Goal, request.Requirements, request.Constraints, request.Assumptions, request.Description, request.Charter);

        if (needsReapproval)
        {
            return await StartApprovalAsync(project, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(project));
    }

    public async Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return Result.Failure(Error.NotFound("Project not found."));
        }

        project.Archive();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ProjectDto>> ChangeStatusAsync(Guid id, ChangeProjectStatusRequest request, CancellationToken cancellationToken)
    {
        var project = await repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return Result.Failure<ProjectDto>(Error.NotFound("Project not found."));
        }

        if (!Enum.IsDefined(request.Status) || request.Status == ProjectStatus.Archived)
        {
            return Result.Failure<ProjectDto>(Error.Validation("Choose Draft, Active, OnHold or Completed; use the archive call to archive a project."));
        }

        if (project.Status == ProjectStatus.Archived)
        {
            return Result.Failure<ProjectDto>(Error.Conflict("An archived project cannot change status."));
        }

        // Working statuses belong to an approved charter; Draft stays open to every project.
        await SyncWithApprovalAsync(project, cancellationToken);
        if (request.Status != ProjectStatus.Draft && request.Status != project.Status && project.ApprovalStatus != ApprovalStatus.Approved)
        {
            return Result.Failure<ProjectDto>(ProjectErrors.NotApproved);
        }

        project.ChangeStatus(request.Status);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(project));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return Result.Failure(Error.NotFound("Project not found."));
        }

        var participants = deletionParticipants?.ToList() ?? [];
        var reasons = new List<string>();
        foreach (var participant in participants)
        {
            if (await participant.GetBlockingReasonAsync(project.TenantId, id, cancellationToken) is { } reason)
            {
                reasons.Add(reason);
            }
        }

        if (reasons.Count > 0)
        {
            return Result.Failure(Error.Conflict($"The project cannot be deleted while it still has: {string.Join("; ", reasons)}. Remove those first, or archive the project instead."));
        }

        await repository.RemoveAsync(project, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var participant in participants)
        {
            await participant.OnProjectDeletedAsync(project.TenantId, id, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<ProjectDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return Result.Failure<ProjectDto>(Error.NotFound("Project not found."));
        }

        // A project already with the approvers, or already approved, is not sent again (a double click, a stale screen).
        // One left "pending" by a failure is repaired first, so it can be sent.
        await SyncWithApprovalAsync(project, cancellationToken);
        if (project.ApprovalStatus == ApprovalStatus.PendingApproval)
        {
            return Result.Failure<ProjectDto>(ProjectErrors.AlreadyPending);
        }

        if (project.ApprovalStatus == ApprovalStatus.Approved)
        {
            return Result.Failure<ProjectDto>(ProjectErrors.AlreadyApproved);
        }

        foreach (var check in submissionChecks ?? [])
        {
            var objection = await check.CheckAsync(project, cancellationToken);
            if (objection is not null)
            {
                return Result.Failure<ProjectDto>(objection);
            }
        }

        return await StartApprovalAsync(project, cancellationToken);
    }

    /// <summary>
    /// Starts the charter's approval and records it on the project. The approval lives in another store: if the project
    /// cannot be saved afterwards, the approval just started is withdrawn, so no approver is left with an orphan.
    /// </summary>
    private async Task<Result<ProjectDto>> StartApprovalAsync(Project project, CancellationToken cancellationToken)
    {
        var subject = SubjectOf(project);
        ApprovalRequestOutcome outcome;
        try
        {
            outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);
        }
        catch (ApprovalRefusedException refused)
        {
            // Nothing was started (no approver for a step, or only the submitter could approve): nothing is saved either.
            return Result.Failure<ProjectDto>(new Error(refused.Code, refused.Message));
        }

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            project.MarkPendingApproval();
        }
        else
        {
            // No Workflow installed: apply the direct-approve business rule immediately.
            project.Approve();
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch when (outcome == ApprovalRequestOutcome.Submitted && approvalTracker is not null)
        {
            await approvalTracker.WithdrawAsync(subject, CancellationToken.None);
            throw;
        }

        return Result.Success(ToDto(project));
    }

    private Error? CheckCharter(
        string name, Guid? managerUserId, Guid? organizationUnitId, DateOnly? startDate, DateOnly? endDate,
        string? goal, string? requirements, string? constraints, string? assumptions, string? description) =>
        options?.ValidateCharter == true
            ? CharterRules.Check(name, managerUserId, organizationUnitId, startDate, endDate, goal, requirements, constraints, assumptions, description)
            : null;

    private static ProjectDto ToDto(Project project) => new(
        project.Id, project.TenantId, project.Name, project.Code, project.Type, project.Status, project.ApprovalStatus,
        project.OwnerUserId, project.ManagerUserId, project.OrganizationUnitId, project.WorkCalendarId,
        project.StartDate, project.EndDate, project.Cost,
        project.Goal, project.Requirements, project.Constraints, project.Assumptions, project.Description, project.Charter,
        project.CreatedAtUtc);
}
