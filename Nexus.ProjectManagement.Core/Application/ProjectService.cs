using Nexus.ProjectManagement.Core.Application.Dtos;
using Nexus.ProjectManagement.Core.Domain;
using NexusCore.Application.Approvals;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Core.Application;

public sealed class ProjectService(
    IProjectRepository repository,
    IProjectManagementUnitOfWork unitOfWork,
    IApprovalRequester approvalRequester) : IProjectService
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

    public async Task<Result<ProjectDto>> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<ProjectDto>(Error.Validation("Name and code are required."));
        }

        if (await repository.CodeExistsAsync(request.TenantId, request.Code, null, cancellationToken))
        {
            return Result.Failure<ProjectDto>(Error.Conflict("A project with this code already exists."));
        }

        var project = new Project(Guid.NewGuid(), request.TenantId, request.Name, request.Code, request.Type, request.ManagerUserId, request.OwnerUserId);
        project.UpdateDetails(
            request.Name, request.Code, request.ManagerUserId, request.OwnerUserId,
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

    public async Task<Result<ProjectDto>> SubmitForApprovalAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return Result.Failure<ProjectDto>(Error.NotFound("Project not found."));
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

    private static ProjectDto ToDto(Project project) => new(
        project.Id, project.TenantId, project.Name, project.Code, project.Type, project.Status, project.ApprovalStatus,
        project.OwnerUserId, project.ManagerUserId, project.OrganizationUnitId, project.WorkCalendarId,
        project.StartDate, project.EndDate, project.Cost,
        project.Goal, project.Requirements, project.Constraints, project.Assumptions, project.Description, project.Charter,
        project.CreatedAtUtc);
}
