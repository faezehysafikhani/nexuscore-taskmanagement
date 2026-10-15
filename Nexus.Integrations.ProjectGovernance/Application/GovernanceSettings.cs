using System.Text.Json;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Waterfall.Application;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Settings;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.Integrations.ProjectGovernance.Application;

/// <summary>The organisation-wide switches: which ways of seeing other people's work are on, and whether a reviewer who
/// rejects a progress report may still confirm a progress figure of their own.</summary>
public sealed record GovernanceSettingsDto(VisibilityRules Visibility, bool AllowConfirmedProgressOnRejection);

/// <summary>Who may see / manage a project's contracts or invoices. Stored with the project's settings.</summary>
public sealed record AccessMatrixDto(bool ViewByAdmin = true, bool ViewByPM = true, bool ViewByTeam = true, bool ManageByAdmin = true, bool ManageByPM = true);

/// <param name="EnabledTabs">The sections shown on the project; null = every section.</param>
public sealed record ProjectGovernanceSettingsDto(Guid ProjectId, IReadOnlyList<string>? EnabledTabs, AccessMatrixDto ContractAccess, AccessMatrixDto InvoiceAccess);

public sealed record SaveProjectGovernanceSettingsRequest(IReadOnlyList<string>? EnabledTabs, AccessMatrixDto? ContractAccess, AccessMatrixDto? InvoiceAccess);

public interface IGovernanceSettingsService
{
    Task<Result<GovernanceSettingsDto>> GetAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<GovernanceSettingsDto>> SaveAsync(Guid tenantId, GovernanceSettingsDto settings, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ProjectGovernanceSettingsDto>>> ListProjectSettingsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<ProjectGovernanceSettingsDto>> GetProjectAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken);
    Task<Result<ProjectGovernanceSettingsDto>> SaveProjectAsync(Guid tenantId, Guid projectId, SaveProjectGovernanceSettingsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the settings as NexusCore SystemSettings (scope "ProjectGovernance"), so no table is added, and answers the
/// extension points that read them: <see cref="IVisibilityRules"/> for Portfolio and the visibility providers,
/// <see cref="IProgressRejectionPolicy"/> for Waterfall.
/// </summary>
public sealed class GovernanceSettingsService(
    IPlatformRepository repository,
    IUnitOfWork unitOfWork,
    IProjectRepository projectRepository) : IGovernanceSettingsService, IVisibilityRules, IProgressRejectionPolicy
{
    public const string Scope = "ProjectGovernance";
    private const string OrganisationKey = "organisation";
    private const string ProjectKeyPrefix = "project:";

    /// <summary>The sections a project can show or hide (its settings section is always there).</summary>
    public static IReadOnlyList<string> KnownTabs { get; } =
        ["charter", "schedule", "team", "risks", "stakeholders", "status", "delays", "docs", "actions", "contracts", "invoices"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Stored(VisibilityRules? Visibility, bool? AllowConfirmedProgressOnRejection);
    private sealed record StoredProject(List<string>? EnabledTabs, AccessMatrixDto? ContractAccess, AccessMatrixDto? InvoiceAccess);

    public async Task<Result<GovernanceSettingsDto>> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Result.Success(await ReadAsync(tenantId, cancellationToken));

    async Task<VisibilityRules> IVisibilityRules.GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await ReadAsync(tenantId, cancellationToken)).Visibility;

    async Task<bool> IProgressRejectionPolicy.AllowsConfirmedProgressAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await ReadAsync(tenantId, cancellationToken)).AllowConfirmedProgressOnRejection;

    public async Task<Result<GovernanceSettingsDto>> SaveAsync(Guid tenantId, GovernanceSettingsDto settings, CancellationToken cancellationToken)
    {
        await WriteAsync(tenantId, OrganisationKey, new Stored(settings.Visibility, settings.AllowConfirmedProgressOnRejection), cancellationToken);
        return Result.Success(settings);
    }

    public async Task<Result<IReadOnlyList<ProjectGovernanceSettingsDto>>> ListProjectSettingsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var all = await repository.ListSettingsAsync(tenantId, cancellationToken);
        var list = new List<ProjectGovernanceSettingsDto>();
        foreach (var setting in all.Where(s => s.TenantId == tenantId && s.Scope == Scope && s.Key.StartsWith(ProjectKeyPrefix, StringComparison.Ordinal)))
        {
            if (Guid.TryParse(setting.Key[ProjectKeyPrefix.Length..], out var projectId))
            {
                list.Add(ToDto(projectId, Parse<StoredProject>(setting.Value)));
            }
        }

        return Result.Success<IReadOnlyList<ProjectGovernanceSettingsDto>>(list);
    }

    public async Task<Result<ProjectGovernanceSettingsDto>> GetProjectAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken)
    {
        var setting = await repository.FindSettingAsync(tenantId, ProjectKeyPrefix + projectId, Scope, cancellationToken);
        return Result.Success(ToDto(projectId, setting is null ? null : Parse<StoredProject>(setting.Value)));
    }

    public async Task<Result<ProjectGovernanceSettingsDto>> SaveProjectAsync(
        Guid tenantId, Guid projectId, SaveProjectGovernanceSettingsRequest request, CancellationToken cancellationToken)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken);
        if (project is null || project.TenantId != tenantId)
        {
            return Result.Failure<ProjectGovernanceSettingsDto>(Error.NotFound("Project was not found."));
        }

        var unknown = request.EnabledTabs?.FirstOrDefault(tab => !KnownTabs.Contains(tab));
        if (unknown is not null)
        {
            return Result.Failure<ProjectGovernanceSettingsDto>(Error.Validation($"Unknown project section '{unknown}'. Use any of: {string.Join(", ", KnownTabs)}."));
        }

        var stored = new StoredProject(request.EnabledTabs?.Distinct().ToList(), request.ContractAccess, request.InvoiceAccess);
        await WriteAsync(tenantId, ProjectKeyPrefix + projectId, stored, cancellationToken);
        return Result.Success(ToDto(projectId, stored));
    }

    private async Task<GovernanceSettingsDto> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var setting = await repository.FindSettingAsync(tenantId, OrganisationKey, Scope, cancellationToken);
        var stored = setting is null ? null : Parse<Stored>(setting.Value);
        return new GovernanceSettingsDto(stored?.Visibility ?? VisibilityRules.AllOn, stored?.AllowConfirmedProgressOnRejection ?? false);
    }

    private async Task WriteAsync<T>(Guid tenantId, string key, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, Json);
        var existing = await repository.FindSettingAsync(tenantId, key, Scope, cancellationToken);
        if (existing is null)
        {
            await repository.AddSettingAsync(new SystemSetting(Guid.NewGuid(), tenantId, key, json, Scope), cancellationToken);
        }
        else
        {
            existing.UpdateValue(json);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static T? Parse<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null; // an unreadable row counts as "never saved"
        }
    }

    private static ProjectGovernanceSettingsDto ToDto(Guid projectId, StoredProject? stored) => new(
        projectId, stored?.EnabledTabs, stored?.ContractAccess ?? new AccessMatrixDto(), stored?.InvoiceAccess ?? new AccessMatrixDto());
}
