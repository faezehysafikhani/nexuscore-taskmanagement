using System.Text.Json;
using Nexus.OrgStructure.Domain;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// A chart drawn by hand: its name, and the companies, deputies (units) and posts chosen for it.
/// The client draws it from the organization chart, showing only what was chosen.
/// </summary>
public sealed record CustomChartDto(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<Guid> CompanyIds,
    IReadOnlyList<Guid> UnitIds,
    IReadOnlyList<Guid> PostIds,
    bool ShowHolders,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc);

public sealed record SaveCustomChartRequest(
    string? Name,
    string? Description,
    IReadOnlyList<Guid>? CompanyIds,
    IReadOnlyList<Guid>? UnitIds,
    IReadOnlyList<Guid>? PostIds,
    bool ShowHolders = true);

public interface ICustomChartService
{
    Task<Result<IReadOnlyList<CustomChartDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<CustomChartDto>> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<Result<CustomChartDto>> CreateAsync(Guid tenantId, SaveCustomChartRequest request, CancellationToken cancellationToken);
    Task<Result<CustomChartDto>> UpdateAsync(Guid tenantId, Guid id, SaveCustomChartRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Saved charts («طراحی چارت دلخواه»). Everything chosen must exist in the tenant. Where everyone
/// is kept to their own company (<see cref="CompanyReachScope"/>), a user sees the charts that
/// show something of their company, and makes or changes only charts of their company alone.
/// </summary>
public sealed class CustomChartService(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    CompanyReachScope? companyReach = null) : ICustomChartService
{
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 1000;

    private sealed record Choice(List<Guid> CompanyIds, List<Guid> UnitIds, List<Guid> PostIds);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<IReadOnlyList<CustomChartDto>>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        var places = await PlacesAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<CustomChartDto>>((await repository.ListCustomChartsAsync(tenantId, cancellationToken))
            .Where(chart => mine is null || places.CompaniesOf(Read(chart)).Overlaps(mine))
            .Select(ToDto)
            .ToList());
    }

    public async Task<Result<CustomChartDto>> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var chart = await repository.GetCustomChartAsync(tenantId, id, cancellationToken);
        if (chart is null || !await MaySeeAsync(tenantId, chart, cancellationToken))
        {
            return Result.Failure<CustomChartDto>(Error.NotFound("Chart was not found."));
        }

        return Result.Success(ToDto(chart));
    }

    public async Task<Result<CustomChartDto>> CreateAsync(Guid tenantId, SaveCustomChartRequest request, CancellationToken cancellationToken)
    {
        var checkedChoice = await ValidateAsync(tenantId, request, cancellationToken);
        if (checkedChoice.IsFailure)
        {
            return Result.Failure<CustomChartDto>(checkedChoice.Error);
        }

        var chart = new CustomChart(Guid.NewGuid(), tenantId, request.Name!, request.Description, Write(checkedChoice.Value), request.ShowHolders);
        await repository.AddCustomChartAsync(chart, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(chart));
    }

    public async Task<Result<CustomChartDto>> UpdateAsync(Guid tenantId, Guid id, SaveCustomChartRequest request, CancellationToken cancellationToken)
    {
        var chart = await repository.GetCustomChartAsync(tenantId, id, cancellationToken);
        if (chart is null || !await MaySeeAsync(tenantId, chart, cancellationToken))
        {
            return Result.Failure<CustomChartDto>(Error.NotFound("Chart was not found."));
        }

        if (!await MayChangeAsync(tenantId, chart, cancellationToken))
        {
            return Result.Failure<CustomChartDto>(OtherCompany());
        }

        var checkedChoice = await ValidateAsync(tenantId, request, cancellationToken);
        if (checkedChoice.IsFailure)
        {
            return Result.Failure<CustomChartDto>(checkedChoice.Error);
        }

        chart.Update(request.Name!, request.Description, Write(checkedChoice.Value), request.ShowHolders);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(chart));
    }

    public async Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var chart = await repository.GetCustomChartAsync(tenantId, id, cancellationToken);
        if (chart is null || !await MaySeeAsync(tenantId, chart, cancellationToken))
        {
            return Result.Failure(Error.NotFound("Chart was not found."));
        }

        if (!await MayChangeAsync(tenantId, chart, cancellationToken))
        {
            return Result.Failure(OtherCompany());
        }

        repository.RemoveCustomChart(chart);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result<Choice>> ValidateAsync(Guid tenantId, SaveCustomChartRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<Choice>(Error.Validation("Give the chart a name."));
        }

        if (request.Name.Trim().Length > MaxNameLength || (request.Description?.Trim().Length ?? 0) > MaxDescriptionLength)
        {
            return Result.Failure<Choice>(Error.Validation("Name or description is too long."));
        }

        var choice = new Choice(
            (request.CompanyIds ?? []).Distinct().ToList(),
            (request.UnitIds ?? []).Distinct().ToList(),
            (request.PostIds ?? []).Distinct().ToList());
        if (choice.CompanyIds.Count + choice.UnitIds.Count + choice.PostIds.Count == 0)
        {
            return Result.Failure<Choice>(Error.Validation("Choose at least one company, deputy or post for the chart."));
        }

        var places = await PlacesAsync(tenantId, cancellationToken);
        if (!choice.CompanyIds.All(places.Companies.Contains)
            || !choice.UnitIds.All(places.CompanyOfUnit.ContainsKey)
            || !choice.PostIds.All(places.CompanyOfPost.ContainsKey))
        {
            return Result.Failure<Choice>(Error.Validation("Something chosen for the chart was not found."));
        }

        // Kept to one's own company: a chart of one's own company alone.
        if (await MyCompaniesAsync(tenantId, cancellationToken) is { } mine && !places.CompaniesOf(choice).IsSubsetOf(mine))
        {
            return Result.Failure<Choice>(OtherCompany());
        }

        return Result.Success(choice);
    }

    private async Task<bool> MaySeeAsync(Guid tenantId, CustomChart chart, CancellationToken cancellationToken) =>
        await MyCompaniesAsync(tenantId, cancellationToken) is not { } mine
        || (await PlacesAsync(tenantId, cancellationToken)).CompaniesOf(Read(chart)).Overlaps(mine);

    private async Task<bool> MayChangeAsync(Guid tenantId, CustomChart chart, CancellationToken cancellationToken) =>
        await MyCompaniesAsync(tenantId, cancellationToken) is not { } mine
        || (await PlacesAsync(tenantId, cancellationToken)).CompaniesOf(Read(chart)).IsSubsetOf(mine);

    private async Task<IReadOnlySet<Guid>?> MyCompaniesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        companyReach is not null && currentUser.UserId is { } me
            ? await companyReach.GetCompaniesAsync(tenantId, me, cancellationToken)
            : null;

    private static Error OtherCompany() => Error.Forbidden("You can only see and change your own company.");

    /// <summary>Which company each deputy and post belongs to.</summary>
    private sealed record Places(HashSet<Guid> Companies, Dictionary<Guid, Guid> CompanyOfUnit, Dictionary<Guid, Guid> CompanyOfPost)
    {
        public HashSet<Guid> CompaniesOf(Choice choice) =>
        [
            .. choice.CompanyIds.Where(Companies.Contains),
            .. choice.UnitIds.Where(CompanyOfUnit.ContainsKey).Select(id => CompanyOfUnit[id]),
            .. choice.PostIds.Where(CompanyOfPost.ContainsKey).Select(id => CompanyOfPost[id]),
        ];
    }

    private async Task<Places> PlacesAsync(Guid tenantId, CancellationToken cancellationToken) => new(
        (await repository.ListCompaniesAsync(tenantId, cancellationToken)).Select(company => company.Id).ToHashSet(),
        (await repository.ListCompanyUnitsAsync(tenantId, cancellationToken)).GroupBy(link => link.UnitId).ToDictionary(g => g.Key, g => g.First().CompanyId),
        (await repository.ListUnitPositionsAsync(tenantId, cancellationToken)).ToDictionary(seat => seat.Id, seat => seat.CompanyId));

    private static Choice Read(CustomChart chart)
    {
        try
        {
            return JsonSerializer.Deserialize<Choice>(chart.Selection, Json) is { } choice
                ? new Choice(choice.CompanyIds ?? [], choice.UnitIds ?? [], choice.PostIds ?? [])
                : new Choice([], [], []);
        }
        catch (JsonException)
        {
            return new Choice([], [], []);
        }
    }

    private static string Write(Choice choice) => JsonSerializer.Serialize(choice, Json);

    private static CustomChartDto ToDto(CustomChart chart)
    {
        var choice = Read(chart);
        return new CustomChartDto(
            chart.Id, chart.Name, chart.Description, choice.CompanyIds, choice.UnitIds, choice.PostIds,
            chart.ShowHolders, chart.CreatedAtUtc, chart.ModifiedAtUtc);
    }
}
