using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Domain;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application;

/// <summary>
/// Optional rule (see <see cref="WaterfallOptions.RequireWeightsTotalOnSubmit"/>): a waterfall project whose activities carry typed weights
/// is sent for approval only when those weights add up to 100, so progress rolls up to a whole.
/// </summary>
public sealed class ScheduleWeightsSubmissionCheck(
    IScheduleService scheduleService, IWaterfallSettingsRepository? settingsRepository = null, WaterfallOptions? options = null) : IProjectSubmissionCheck
{
    public const string ErrorCode = "project.schedule_weights_not_100";

    public async Task<Error?> CheckAsync(Project project, CancellationToken cancellationToken)
    {
        if (options?.RequireWeightsTotalOnSubmit != true || project.Type != ProjectType.Waterfall)
        {
            return null;
        }

        var settings = settingsRepository is null ? null : await settingsRepository.GetAsync(project.Id, cancellationToken);
        if ((settings?.Weighting ?? WeightingMode.Manual) != WeightingMode.Manual)
        {
            return null; // the other modes always add up to 100 by construction
        }

        var schedule = await scheduleService.GetScheduleAsync(project.Id, cancellationToken);
        if (schedule.IsFailure || schedule.Value!.Activities.Count == 0)
        {
            return null;
        }

        var total = Math.Round(schedule.Value.Activities.Where(a => !a.IsSummary).Sum(a => a.Weight));
        return total == 100m
            ? null
            : new Error(ErrorCode, $"The weights of the activities add up to {total}, not 100; fix them in the schedule before sending the project.");
    }
}
