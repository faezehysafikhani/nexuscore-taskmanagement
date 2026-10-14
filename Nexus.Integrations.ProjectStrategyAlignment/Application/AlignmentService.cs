using Nexus.Integrations.StrategyAlignment.Application.Dtos;
using Nexus.Integrations.StrategyAlignment.Domain;
using Nexus.ProjectManagement.Core.Application;
using Nexus.StrategyManagement.Application;
using Nexus.StrategyManagement.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.StrategyAlignment.Application;

/// <summary>
/// Owns the Project x Strategy relationship. Both Project and Strategy stay entirely unaware
/// of alignment - this service is the only place that reads from both of their repositories.
/// </summary>
public sealed class AlignmentService(
    IAlignmentRepository repository,
    IProjectRepository projectRepository,
    IStrategyRepository strategyRepository,
    IStrategyAlignmentUnitOfWork unitOfWork) : IAlignmentService
{
    public async Task<Result<IReadOnlyList<ProjectStrategyAlignmentDto>>> ListAsync(Guid tenantId, Guid? projectId, Guid? strategyId, CancellationToken cancellationToken)
    {
        var alignments = await repository.ListAsync(tenantId, projectId, strategyId, cancellationToken);
        return Result.Success<IReadOnlyList<ProjectStrategyAlignmentDto>>(alignments.Select(ToDto).ToList());
    }

    public async Task<Result<ProjectStrategyAlignmentDto>> CreateAsync(CreateAlignmentRequest request, CancellationToken cancellationToken)
    {
        if (await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken) is null)
        {
            return Result.Failure<ProjectStrategyAlignmentDto>(Error.Validation("Project was not found."));
        }

        if (await strategyRepository.GetByIdAsync(request.StrategyId, cancellationToken) is null)
        {
            return Result.Failure<ProjectStrategyAlignmentDto>(Error.Validation("Strategy was not found."));
        }

        var existing = await repository.ListAsync(request.TenantId, request.ProjectId, request.StrategyId, cancellationToken);
        if (existing.Count > 0)
        {
            return Result.Failure<ProjectStrategyAlignmentDto>(Error.Conflict("This project and strategy are already linked - update the existing alignment instead."));
        }

        var alignment = new ProjectStrategyAlignment(Guid.NewGuid(), request.TenantId, request.ProjectId, request.StrategyId, request.AlignmentLevel);
        alignment.Update(request.AlignmentLevel, request.AlignmentPercentage);

        await repository.AddAsync(alignment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(alignment));
    }

    public async Task<Result<ProjectStrategyAlignmentDto>> UpdateAsync(Guid id, UpdateAlignmentRequest request, CancellationToken cancellationToken)
    {
        var alignment = await repository.GetByIdAsync(id, cancellationToken);
        if (alignment is null)
        {
            return Result.Failure<ProjectStrategyAlignmentDto>(Error.NotFound("Alignment not found."));
        }

        alignment.Update(request.AlignmentLevel, request.AlignmentPercentage);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(alignment));
    }

    /// <summary>
    /// The percentage of each project: for every leaf strategy that carries weight, the project's level (None 0, Low 1, Medium 2, High 3)
    /// times the weight, over what High everywhere would score. Only strategies that exist and are leaves count; rows left behind by a
    /// deleted strategy are ignored.
    /// </summary>
    public async Task<Result<IReadOnlyList<ProjectAlignmentScoreDto>>> GetScoresAsync(
        Guid tenantId, ProjectAlignmentScoresRequest request, CancellationToken cancellationToken)
    {
        var projectIds = request.ProjectIds.Distinct().ToList();
        if (projectIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<ProjectAlignmentScoreDto>>([]);
        }

        var leaves = StrategyTree.Leaves(await strategyRepository.ListAsync(tenantId, cancellationToken));
        var rows = (await repository.ListAsync(tenantId, null, null, cancellationToken)).Where(a => projectIds.Contains(a.ProjectId)).ToList();
        return Result.Success<IReadOnlyList<ProjectAlignmentScoreDto>>(projectIds.Select(id => Score(id, leaves, rows)).ToList());
    }

    /// <summary>Sets the levels of one project for several leaf strategies, then returns the project's score.</summary>
    public async Task<Result<ProjectAlignmentScoreDto>> SetLevelsAsync(SetProjectAlignmentRequest request, CancellationToken cancellationToken)
    {
        if (await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken) is null)
        {
            return Result.Failure<ProjectAlignmentScoreDto>(Error.Validation("Project was not found."));
        }

        if (request.Levels.Any(l => !Enum.IsDefined(l.Level)))
        {
            return Result.Failure<ProjectAlignmentScoreDto>(Error.Validation("Unknown alignment level."));
        }

        var leaves = StrategyTree.Leaves(await strategyRepository.ListAsync(request.TenantId, cancellationToken));
        var leafIds = leaves.Select(l => l.Id).ToHashSet();
        if (request.Levels.Any(l => !leafIds.Contains(l.StrategyId)))
        {
            return Result.Failure<ProjectAlignmentScoreDto>(Error.Validation("Alignment is set on strategies that have no sub-strategies."));
        }

        var rows = (await repository.ListAsync(request.TenantId, request.ProjectId, null, cancellationToken)).ToList();
        foreach (var asked in request.Levels.GroupBy(l => l.StrategyId).Select(g => g.Last()))
        {
            var row = rows.SingleOrDefault(a => a.StrategyId == asked.StrategyId);
            if (row is null)
            {
                row = new ProjectStrategyAlignment(Guid.NewGuid(), request.TenantId, request.ProjectId, asked.StrategyId, asked.Level);
                rows.Add(row);
                await repository.AddAsync(row, cancellationToken);
            }

            row.Update(asked.Level, null);
        }

        var score = Score(request.ProjectId, leaves, rows);
        foreach (var row in rows)
        {
            row.Update(row.AlignmentLevel, score.Percentage);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(score);
    }

    private static ProjectAlignmentScoreDto Score(Guid projectId, IReadOnlyList<StrategyNodeDto> leaves, IEnumerable<ProjectStrategyAlignment> rows)
    {
        var mine = rows.Where(a => a.ProjectId == projectId).ToDictionary(a => a.StrategyId, a => a.AlignmentLevel);
        decimal score = 0;
        decimal best = 0;
        var levels = new List<StrategyLevelDto>();
        foreach (var leaf in leaves)
        {
            var level = mine.GetValueOrDefault(leaf.Id, AlignmentLevel.None);
            levels.Add(new StrategyLevelDto(leaf.Id, level));
            if (leaf.EffectiveWeight > 0)
            {
                score += (int)level * leaf.EffectiveWeight;
                best += (int)AlignmentLevel.High * leaf.EffectiveWeight;
            }
        }

        return new ProjectAlignmentScoreDto(projectId, levels, best > 0 ? score / best * 100 : 0);
    }

    private static ProjectStrategyAlignmentDto ToDto(ProjectStrategyAlignment alignment) =>
        new(alignment.Id, alignment.TenantId, alignment.ProjectId, alignment.StrategyId, alignment.AlignmentLevel, alignment.AlignmentPercentage);
}
