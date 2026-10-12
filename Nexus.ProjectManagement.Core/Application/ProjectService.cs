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
    IEnumerable<IProjectSubmissionCheck>? submissionChecks = null) : IProjectService
{
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
        return project is null
            ? Result.Failure<ProjectDto>(Error.NotFound("Project not found."))
            : Result.Success(ToDto(project));
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

        project.UpdateDetails(
            request.Name, request.Code, request.ManagerUserId, request.OwnerUserId,
            request.OrganizationUnitId, request.WorkCalendarId, request.StartDate, request.EndDate, request.Cost,
            request.Goal, request.Requirements, request.Constraints, request.Assumptions, request.Description, request.Charter);

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

        foreach (var check in submissionChecks ?? [])
        {
            var objection = await check.CheckAsync(project, cancellationToken);
            if (objection is not null)
            {
                return Result.Failure<ProjectDto>(objection);
            }
        }

        var subject = new ApprovalSubject("Project", project.Id, project.TenantId);
        var outcome = await approvalRequester.RequestApprovalAsync(subject, cancellationToken);

        if (outcome == ApprovalRequestOutcome.Submitted)
        {
            project.MarkPendingApproval();
        }
        else
        {
            // No Workflow installed: apply the direct-approve business rule immediately.
            project.Approve();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
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
