using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

public sealed class ActivityDependencyService(
    IActivityDependencyRepository repository,
    IActivityRepository activityRepository,
    IWaterfallUnitOfWork unitOfWork) : IActivityDependencyService
{
    public async Task<Result<IReadOnlyList<ActivityDependencyDto>>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var dependencies = await repository.ListByProjectAsync(projectId, cancellationToken);
        return Result.Success<IReadOnlyList<ActivityDependencyDto>>(dependencies.Select(ToDto).ToList());
    }

    public async Task<Result<ActivityDependencyDto>> CreateAsync(CreateActivityDependencyRequest request, CancellationToken cancellationToken)
    {
        if (request.PredecessorActivityId == request.SuccessorActivityId)
        {
            return Result.Failure<ActivityDependencyDto>(Error.Validation("An activity cannot depend on itself."));
        }

        var activities = await activityRepository.ListByProjectAsync(request.ProjectId, cancellationToken);
        var predecessor = activities.SingleOrDefault(a => a.Id == request.PredecessorActivityId);
        var successor = activities.SingleOrDefault(a => a.Id == request.SuccessorActivityId);
        if (predecessor is null || successor is null)
        {
            return Result.Failure<ActivityDependencyDto>(Error.NotFound("Both activities must exist in the project."));
        }

        // A summary activity's dates are the span of its children, so a link to it would have to
        // push every child; links connect the leaf activities that actually carry the work.
        if (activities.Any(a => a.ParentActivityId == predecessor.Id) || activities.Any(a => a.ParentActivityId == successor.Id))
        {
            return Result.Failure<ActivityDependencyDto>(Error.Validation("Dependencies connect activities without sub-activities."));
        }

        var existing = await repository.ListByProjectAsync(request.ProjectId, cancellationToken);
        if (existing.Any(d => d.PredecessorActivityId == predecessor.Id && d.SuccessorActivityId == successor.Id))
        {
            return Result.Failure<ActivityDependencyDto>(Error.Conflict("These activities are already linked."));
        }

        if (DependencyGraph.WouldCreateCycle(
                existing.Select(d => (d.PredecessorActivityId, d.SuccessorActivityId)), predecessor.Id, successor.Id))
        {
            return Result.Failure<ActivityDependencyDto>(Error.Conflict("This link would create a circular dependency."));
        }

        var dependency = new ActivityDependency(
            Guid.NewGuid(), request.TenantId, request.ProjectId, predecessor.Id, successor.Id, request.Type, request.LagDays);
        await repository.AddAsync(dependency, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(dependency));
    }

    public async Task<Result<ActivityDependencyDto>> UpdateAsync(Guid id, UpdateActivityDependencyRequest request, CancellationToken cancellationToken)
    {
        var dependency = await repository.GetByIdAsync(id, cancellationToken);
        if (dependency is null)
        {
            return Result.Failure<ActivityDependencyDto>(Error.NotFound("Dependency not found."));
        }

        dependency.Update(request.Type, request.LagDays);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(dependency));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var dependency = await repository.GetByIdAsync(id, cancellationToken);
        if (dependency is null)
        {
            return Result.Failure(Error.NotFound("Dependency not found."));
        }

        await repository.RemoveAsync(dependency, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static ActivityDependencyDto ToDto(ActivityDependency dependency) => new(
        dependency.Id, dependency.TenantId, dependency.ProjectId, dependency.PredecessorActivityId,
        dependency.SuccessorActivityId, dependency.Type, dependency.LagDays);
}
