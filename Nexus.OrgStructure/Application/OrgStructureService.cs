using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

public interface IOrgStructureService
{
    Task<Result<IReadOnlyList<CompanyDto>>> ListCompaniesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<CompanyDto>> GetCompanyAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<Result<CompanyDto>> CreateCompanyAsync(Guid tenantId, CreateCompanyRequest request, CancellationToken cancellationToken);
    Task<Result<CompanyDto>> UpdateCompanyAsync(Guid tenantId, Guid id, UpdateCompanyRequest request, CancellationToken cancellationToken);
    Task<Result> DeleteCompanyAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<CompanyUnitDto>>> ListCompanyUnitsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<CompanyUnitDto>> LinkUnitAsync(Guid tenantId, Guid unitId, LinkUnitRequest request, CancellationToken cancellationToken);
    Task<Result> UnlinkUnitAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<PositionDto>>> ListPositionsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<PositionDto>> CreatePositionAsync(Guid tenantId, CreatePositionRequest request, CancellationToken cancellationToken);
    Task<Result<PositionDto>> UpdatePositionAsync(Guid tenantId, Guid id, UpdatePositionRequest request, CancellationToken cancellationToken);
    Task<Result> DeletePositionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<UnitPositionDto>>> ListUnitPositionsAsync(Guid tenantId, Guid? unitId, CancellationToken cancellationToken);
    Task<Result<UnitPositionDto>> AddUnitPositionAsync(Guid tenantId, Guid unitId, AddUnitPositionRequest request, CancellationToken cancellationToken);
    Task<Result<UnitPositionDto>> AddCompanyPositionAsync(Guid tenantId, Guid companyId, AddUnitPositionRequest request, CancellationToken cancellationToken);
    Task<Result<UnitPositionDto>> UpdateUnitPositionAsync(Guid tenantId, Guid unitPositionId, UpdateUnitPositionRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<UnitPositionDto>>> OrderUnitPositionsAsync(Guid tenantId, Guid unitId, OrderUnitPositionsRequest request, CancellationToken cancellationToken);
    Task<Result> RemoveUnitPositionAsync(Guid tenantId, Guid unitPositionId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<UserAssignmentDto>>> ListAssignmentsAsync(Guid tenantId, Guid? companyId, Guid? unitId, CancellationToken cancellationToken);
    Task<Result<UserAssignmentDto>> GetAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<Result<UserAssignmentDto>> SetAssignmentAsync(Guid tenantId, Guid userId, SetUserAssignmentRequest request, CancellationToken cancellationToken);
    Task<Result> RemoveAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<Result<ManagerDto?>> GetManagerAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<SubordinateDto>>> GetSubordinatesAsync(Guid tenantId, Guid userId, bool includeIndirect, CancellationToken cancellationToken);
    Task<Result<MyPlacementDto>> GetPlacementAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<OrgChartCompanyDto>>> GetChartAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<OrgChartTeamDto>>> ListTeamsOutsideUnitsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<UnplacedUserDto>>> ListUnplacedUsersAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed class OrgStructureService(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    IOrganizationUnitRepository units,
    IUserDirectory users,
    IWorkTeamDirectory workTeams) : IOrgStructureService
{
    private const int MaxNameLength = 200;
    private const int MaxTitleLength = 120;
    private const int MaxDescriptionLength = 1000;
    private const int MaxCodeLength = 40;

    // ---------------------------------------------------------------- companies

    public async Task<Result<IReadOnlyList<CompanyDto>>> ListCompaniesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var companies = await repository.ListCompaniesAsync(tenantId, cancellationToken);
        var links = await repository.ListCompanyUnitsAsync(tenantId, cancellationToken);
        var people = await PeopleAsync(companies.Select(c => c.ManagerUserId), cancellationToken);
        return Result.Success<IReadOnlyList<CompanyDto>>(companies.Select(c => ToDto(c, links, people)).ToList());
    }

    public async Task<Result<CompanyDto>> GetCompanyAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(tenantId, id, cancellationToken);
        if (company is null)
        {
            return Result.Failure<CompanyDto>(Error.NotFound("Company was not found."));
        }

        return Result.Success(await ToDtoAsync(tenantId, company, cancellationToken));
    }

    public async Task<Result<CompanyDto>> CreateCompanyAsync(Guid tenantId, CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        var invalid = await ValidateCompanyAsync(tenantId, null, request.Name, request.Code, request.Description, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<CompanyDto>(invalid);
        }

        // No head yet: whoever is given the company's position with no superior becomes it.
        var company = new Company(Guid.NewGuid(), tenantId, request.Name, request.Code, request.Description, managerUserId: null);
        await repository.AddCompanyAsync(company, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(tenantId, company, cancellationToken));
    }

    public async Task<Result<CompanyDto>> UpdateCompanyAsync(Guid tenantId, Guid id, UpdateCompanyRequest request, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(tenantId, id, cancellationToken);
        if (company is null)
        {
            return Result.Failure<CompanyDto>(Error.NotFound("Company was not found."));
        }

        var invalid = await ValidateCompanyAsync(tenantId, id, request.Name, request.Code, request.Description, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<CompanyDto>(invalid);
        }

        company.Update(request.Name, request.Code, request.Description, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(tenantId, company, cancellationToken));
    }

    public async Task<Result> DeleteCompanyAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(tenantId, id, cancellationToken);
        if (company is null)
        {
            return Result.Failure(Error.NotFound("Company was not found."));
        }

        var inUse = (await repository.ListCompanyUnitsAsync(tenantId, cancellationToken)).Any(link => link.CompanyId == id)
            || (await repository.ListAssignmentsAsync(tenantId, cancellationToken)).Any(assignment => assignment.CompanyId == id)
            || (await repository.ListUnitPositionsAsync(tenantId, cancellationToken)).Any(seat => seat.CompanyId == id);
        if (inUse)
        {
            return Result.Failure(Error.Conflict("The company still has units or people. Move them first, or deactivate the company instead."));
        }

        repository.RemoveCompany(company);
        foreach (var link in (await repository.ListPositionCompaniesAsync(tenantId, cancellationToken)).Where(link => link.CompanyId == id))
        {
            repository.RemovePositionCompany(link);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ValidateCompanyAsync(
        Guid tenantId, Guid? id, string? name, string? code, string? description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return Error.Validation("Name and code are required.");
        }

        if (name.Trim().Length > MaxNameLength || code.Trim().Length > 40 || (description?.Trim().Length ?? 0) > MaxDescriptionLength)
        {
            return Error.Validation("Name, code or description is too long.");
        }

        if (await repository.CompanyCodeExistsAsync(tenantId, code.Trim(), id, cancellationToken))
        {
            return Error.Conflict("A company with this code already exists.");
        }

        return null;
    }

    // ---------------------------------------------------------------- company units

    public async Task<Result<IReadOnlyList<CompanyUnitDto>>> ListCompanyUnitsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<CompanyUnitDto>>(snapshot.Links
            .Select(link => ToDto(link, snapshot))
            .OrderBy(dto => dto.CompanyName).ThenBy(dto => dto.UnitName)
            .ToList());
    }

    public async Task<Result<CompanyUnitDto>> LinkUnitAsync(Guid tenantId, Guid unitId, LinkUnitRequest request, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (!snapshot.Units.ContainsKey(unitId))
        {
            return Result.Failure<CompanyUnitDto>(Error.NotFound("Organization unit was not found."));
        }

        var company = snapshot.Companies.GetValueOrDefault(request.CompanyId);
        if (company is null)
        {
            return Result.Failure<CompanyUnitDto>(Error.NotFound("Company was not found."));
        }

        if (!company.IsActive)
        {
            return Result.Failure<CompanyUnitDto>(Error.Validation("The company is inactive."));
        }

        var resolver = snapshot.Resolver();

        var subtree = resolver.SubtreeOf(unitId);
        if (snapshot.Links.Any(link => link.UnitId != unitId && subtree.Contains(link.UnitId)))
        {
            return Result.Failure<CompanyUnitDto>(Error.Validation("Units below this one are linked to a company. Unlink them first."));
        }

        if (snapshot.Assignments.Any(a => a.UnitId is { } u && subtree.Contains(u) && a.CompanyId != request.CompanyId))
        {
            return Result.Failure<CompanyUnitDto>(Error.Conflict(
                "People of another company are assigned to this unit or a unit below it. Move them first."));
        }

        var moving = snapshot.Seats.Where(seat => seat.UnitId is { } u && subtree.Contains(u)).ToList();
        if (moving.Any(seat => !snapshot.IsFor(seat.PositionId, request.CompanyId)))
        {
            return Result.Failure<CompanyUnitDto>(Error.Conflict(
                "Positions of this unit are not for that company. Add the company to those positions first."));
        }

        if (moving.Any(seat => seat.CompanyId != request.CompanyId && snapshot.Seats.Any(other =>
                other.CompanyId == request.CompanyId && string.Equals(other.Code, seat.Code, StringComparison.OrdinalIgnoreCase))))
        {
            return Result.Failure<CompanyUnitDto>(Error.Conflict(
                "Post codes of this unit are already used in that company. Change them first."));
        }

        moving.ForEach(seat => seat.MoveToCompany(request.CompanyId));

        var existing = snapshot.Links.FirstOrDefault(link => link.UnitId == unitId);
        if (existing is null)
        {
            existing = new CompanyUnit(Guid.NewGuid(), tenantId, request.CompanyId, unitId);
            await repository.AddCompanyUnitAsync(existing, cancellationToken);
        }
        else
        {
            existing.MoveTo(request.CompanyId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(existing, snapshot));
    }

    public async Task<Result> UnlinkUnitAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken)
    {
        var link = await repository.GetCompanyUnitAsync(tenantId, unitId, cancellationToken);
        if (link is null)
        {
            return Result.Failure(Error.NotFound("This unit is not linked to a company."));
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var subtree = snapshot.Resolver().SubtreeOf(unitId);
        if (snapshot.Assignments.Any(a => a.UnitId is { } u && subtree.Contains(u)))
        {
            return Result.Failure(Error.Conflict("People are assigned to this unit or a unit below it. Move them first."));
        }

        if (snapshot.Seats.Any(seat => seat.UnitId is { } u && subtree.Contains(u)))
        {
            return Result.Failure(Error.Conflict("The unit has posts. Remove them first."));
        }

        repository.RemoveCompanyUnit(link);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ---------------------------------------------------------------- positions

    public async Task<Result<IReadOnlyList<PositionDto>>> ListPositionsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var links = await repository.ListPositionCompaniesAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<PositionDto>>(
            (await repository.ListPositionsAsync(tenantId, cancellationToken)).Select(position => ToDto(position, links)).ToList());
    }

    public async Task<Result<PositionDto>> CreatePositionAsync(Guid tenantId, CreatePositionRequest request, CancellationToken cancellationToken)
    {
        var invalid = await ValidatePositionAsync(tenantId, null, request.Title, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<PositionDto>(invalid);
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (request.ParentPositionId is { } parentId && !snapshot.Positions.ContainsKey(parentId))
        {
            return Result.Failure<PositionDto>(Error.Validation("Parent position was not found."));
        }

        var companyIds = (request.CompanyIds ?? []).Distinct().ToList();
        var position = new Position(Guid.NewGuid(), tenantId, request.Title, request.ParentPositionId);
        var wrong = ValidatePositionCompanies(snapshot, position.Id, request.ParentPositionId, companyIds, companyIds);
        if (wrong is not null)
        {
            return Result.Failure<PositionDto>(wrong);
        }

        await repository.AddPositionAsync(position, cancellationToken);
        foreach (var companyId in companyIds)
        {
            await repository.AddPositionCompanyAsync(new PositionCompany(Guid.NewGuid(), tenantId, position.Id, companyId), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(position, await repository.ListPositionCompaniesAsync(tenantId, cancellationToken)));
    }

    public async Task<Result<PositionDto>> UpdatePositionAsync(Guid tenantId, Guid id, UpdatePositionRequest request, CancellationToken cancellationToken)
    {
        var position = await repository.GetPositionAsync(tenantId, id, cancellationToken);
        if (position is null)
        {
            return Result.Failure<PositionDto>(Error.NotFound("Position was not found."));
        }

        var invalid = await ValidatePositionAsync(tenantId, id, request.Title, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<PositionDto>(invalid);
        }

        if (request.ParentPositionId == id)
        {
            return Result.Failure<PositionDto>(Error.Validation("A position cannot be its own parent."));
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (request.ParentPositionId is { } parentId && !snapshot.Positions.ContainsKey(parentId))
        {
            return Result.Failure<PositionDto>(Error.Validation("Parent position was not found."));
        }

        // Prevent cycles: walk up from the new parent and ensure we never hit this position.
        if (request.ParentPositionId is { } newParent)
        {
            var visited = new HashSet<Guid>();
            var current = newParent;
            while (true)
            {
                if (!visited.Add(current)) break;
                if (current == id)
                {
                    return Result.Failure<PositionDto>(Error.Conflict("This change would create a position hierarchy loop."));
                }

                var parent = snapshot.Positions.GetValueOrDefault(current)?.ParentPositionId;
                if (parent is null) break;
                current = parent.Value;
            }
        }

        // Without a list (older clients) the position keeps its companies.
        var currentCompanies = snapshot.CompaniesOf(id);
        var companyIds = request.CompanyIds is null ? currentCompanies.ToList() : request.CompanyIds.Distinct().ToList();
        var parentChanged = request.ParentPositionId != position.ParentPositionId;
        var companiesChanged = !currentCompanies.SetEquals(companyIds);

        // What is already so stays valid; only a change is checked against the rules.
        if (parentChanged || companiesChanged)
        {
            var newlyRoot = request.ParentPositionId is null && position.ParentPositionId is not null;
            var toCheckForRoot = newlyRoot ? companyIds : companyIds.Where(c => !currentCompanies.Contains(c)).ToList();
            var wrong = ValidatePositionCompanies(snapshot, id, request.ParentPositionId, companyIds, toCheckForRoot);
            if (wrong is not null)
            {
                return Result.Failure<PositionDto>(wrong);
            }
        }

        foreach (var removed in currentCompanies.Where(c => !companyIds.Contains(c)))
        {
            var resolver = snapshot.Resolver();
            var inUse = snapshot.Assignments.Any(a => a.CompanyId == removed && a.PositionId == id)
                || snapshot.Seats.Any(seat => seat.PositionId == id && seat.CompanyId == removed)
                || snapshot.Positions.Values.Any(p => p.ParentPositionId == id && snapshot.IsFor(p.Id, removed));
            if (inUse)
            {
                return Result.Failure<PositionDto>(Error.Conflict(
                    "The position is in use in a company being taken from it (its people, deputies or the positions under it). Change those first."));
            }
        }

        // The head's position stays without a superior while someone is head through it.
        if (request.ParentPositionId is not null && position.ParentPositionId is null
            && snapshot.Companies.Values.Any(company => company.ManagerUserId is { } head
                && snapshot.Assignments.Any(a => a.UserId == head && a.CompanyId == company.Id && a.UnitId is null && a.PositionId == id)))
        {
            return Result.Failure<PositionDto>(Error.Conflict(
                "Someone holds this position as their company's head. Take it from them first."));
        }

        position.Update(request.Title, request.ParentPositionId, request.IsActive);
        foreach (var link in snapshot.PositionCompanies.Where(link => link.PositionId == id && !companyIds.Contains(link.CompanyId)))
        {
            repository.RemovePositionCompany(link);
        }

        foreach (var companyId in companyIds.Where(c => !currentCompanies.Contains(c)))
        {
            await repository.AddPositionCompanyAsync(new PositionCompany(Guid.NewGuid(), tenantId, id, companyId), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(position, await repository.ListPositionCompaniesAsync(tenantId, cancellationToken)));
    }

    /// <summary>
    /// A position is for at least one existing company; its superior is for every one of them;
    /// and in each of <paramref name="checkRootIn"/>, a position with no superior must be the only one.
    /// </summary>
    private static Error? ValidatePositionCompanies(
        Snapshot snapshot, Guid positionId, Guid? parentId, IReadOnlyList<Guid> companyIds, IReadOnlyList<Guid> checkRootIn)
    {
        if (companyIds.Count == 0)
        {
            return Error.Validation("Choose at least one company for the position.");
        }

        if (!companyIds.All(snapshot.Companies.ContainsKey))
        {
            return Error.Validation("Company was not found.");
        }

        if (parentId is { } parent && !companyIds.All(companyId => snapshot.IsFor(parent, companyId)))
        {
            return Error.Validation("The superior position must be for every company of this position.");
        }

        if (parentId is null && checkRootIn.Any(companyId => snapshot.PositionCompanies.Any(link =>
                link.CompanyId == companyId && link.PositionId != positionId
                && snapshot.Positions.GetValueOrDefault(link.PositionId) is { ParentPositionId: null })))
        {
            return Error.Conflict("This company already has a position with no superior (its head's). Choose a superior for this position.");
        }

        return null;
    }

    public async Task<Result> DeletePositionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var position = await repository.GetPositionAsync(tenantId, id, cancellationToken);
        if (position is null)
        {
            return Result.Failure(Error.NotFound("Position was not found."));
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (snapshot.Positions.Values.Any(p => p.ParentPositionId == id))
        {
            return Result.Failure(Error.Conflict("This position has child positions. Reassign or remove them first."));
        }

        if (snapshot.Assignments.Any(a => a.PositionId == id))
        {
            return Result.Failure(Error.Conflict("People hold this position. Change their position first, or deactivate it instead."));
        }

        if (snapshot.Seats.Any(seat => seat.PositionId == id))
        {
            return Result.Failure(Error.Conflict("Units have this position. Remove it from them first, or deactivate it instead."));
        }

        repository.RemovePosition(position);
        foreach (var link in snapshot.PositionCompanies.Where(link => link.PositionId == id))
        {
            repository.RemovePositionCompany(link);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ValidatePositionAsync(Guid tenantId, Guid? id, string? title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("Title is required.");
        }

        if (title.Trim().Length > MaxTitleLength)
        {
            return Error.Validation("Title is too long.");
        }

        return await repository.PositionTitleExistsAsync(tenantId, title.Trim(), id, cancellationToken)
            ? Error.Conflict("A position with this title already exists.")
            : null;
    }

    // ---------------------------------------------------------------- posts (unit positions)

    /// <summary>The posts of one unit (or every post, those directly in companies too), in order, with their holder.</summary>
    public async Task<Result<IReadOnlyList<UnitPositionDto>>> ListUnitPositionsAsync(
        Guid tenantId, Guid? unitId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var seats = snapshot.Seats
            .Where(seat => (unitId is null || seat.UnitId == unitId) && snapshot.IsShown(seat))
            .ToList();
        var seatIds = seats.Select(seat => seat.Id).ToHashSet();
        var people = await PeopleAsync(
            snapshot.Holdings.Where(h => seatIds.Contains(h.UnitPositionId)).Select(h => (Guid?)h.UserId), cancellationToken);
        return Result.Success<IReadOnlyList<UnitPositionDto>>(seats
            .Select(seat => ToDto(seat, snapshot, people))
            .OrderBy(dto => dto.CompanyName).ThenBy(dto => dto.UnitId is not null).ThenBy(dto => dto.UnitName)
            .ThenBy(dto => dto.SortOrder).ThenBy(dto => dto.Title).ThenBy(dto => dto.Code, CodeOrder.Instance)
            .ToList());
    }

    /// <summary>A new post of the position in the unit (a unit may have several of one position).</summary>
    public async Task<Result<UnitPositionDto>> AddUnitPositionAsync(
        Guid tenantId, Guid unitId, AddUnitPositionRequest request, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (!snapshot.Units.ContainsKey(unitId))
        {
            return Result.Failure<UnitPositionDto>(Error.NotFound("Organization unit was not found."));
        }

        if (snapshot.Resolver().CompanyOfUnit(unitId) is not { } unitCompany)
        {
            return Result.Failure<UnitPositionDto>(Error.Validation("Link the unit to a company first."));
        }

        return await AddPostAsync(tenantId, snapshot, unitCompany, unitId, request, cancellationToken);
    }

    /// <summary>A new post directly in the company (in no unit), such as the head's or a board member's.</summary>
    public async Task<Result<UnitPositionDto>> AddCompanyPositionAsync(
        Guid tenantId, Guid companyId, AddUnitPositionRequest request, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (!snapshot.Companies.ContainsKey(companyId))
        {
            return Result.Failure<UnitPositionDto>(Error.NotFound("Company was not found."));
        }

        // The head's post: one per company.
        if (snapshot.Positions.GetValueOrDefault(request.PositionId) is { ParentPositionId: null }
            && snapshot.Seats.Any(seat => seat.CompanyId == companyId && snapshot.IsHeadPost(seat)))
        {
            return Result.Failure<UnitPositionDto>(Error.Conflict("The company already has the head's post."));
        }

        return await AddPostAsync(tenantId, snapshot, companyId, null, request, cancellationToken);
    }

    private async Task<Result<UnitPositionDto>> AddPostAsync(
        Guid tenantId, Snapshot snapshot, Guid companyId, Guid? unitId, AddUnitPositionRequest request, CancellationToken cancellationToken)
    {
        if (snapshot.Positions.GetValueOrDefault(request.PositionId) is not { IsActive: true })
        {
            return Result.Failure<UnitPositionDto>(Error.Validation("Position was not found or is inactive."));
        }

        if (!snapshot.IsFor(request.PositionId, companyId))
        {
            return Result.Failure<UnitPositionDto>(Error.Validation(unitId is null
                ? "This position is not for the company."
                : "This position is not for the unit's company."));
        }

        var code = string.IsNullOrWhiteSpace(request.Code) ? NextCode(snapshot, companyId, []) : request.Code.Trim();
        if (ValidateCode(snapshot, companyId, code, null) is { } invalid)
        {
            return Result.Failure<UnitPositionDto>(invalid);
        }

        var added = new UnitPosition(Guid.NewGuid(), tenantId, companyId, unitId, request.PositionId, NextSortOrder(snapshot, companyId, unitId, request.PositionId), code);
        await repository.AddUnitPositionAsync(added, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(added, snapshot, new Dictionary<Guid, UserContact>()));
    }

    /// <summary>Changes a post's code (unique in its company).</summary>
    public async Task<Result<UnitPositionDto>> UpdateUnitPositionAsync(
        Guid tenantId, Guid unitPositionId, UpdateUnitPositionRequest request, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var seat = snapshot.Seats.FirstOrDefault(s => s.Id == unitPositionId);
        if (seat is null)
        {
            return Result.Failure<UnitPositionDto>(Error.NotFound("Unit position was not found."));
        }

        var code = request.Code?.Trim() ?? string.Empty;
        if (ValidateCode(snapshot, seat.CompanyId, code, seat.Id) is { } invalid)
        {
            return Result.Failure<UnitPositionDto>(invalid);
        }

        seat.ChangeCode(code);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var people = await PeopleAsync(snapshot.Holdings.Where(h => h.UnitPositionId == seat.Id).Select(h => (Guid?)h.UserId), cancellationToken);
        return Result.Success(ToDto(seat, snapshot, people));
    }

    public async Task<Result<IReadOnlyList<UnitPositionDto>>> OrderUnitPositionsAsync(
        Guid tenantId, Guid unitId, OrderUnitPositionsRequest request, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (!snapshot.Units.ContainsKey(unitId))
        {
            return Result.Failure<IReadOnlyList<UnitPositionDto>>(Error.NotFound("Organization unit was not found."));
        }

        var seats = snapshot.Seats.Where(seat => seat.UnitId == unitId).ToDictionary(seat => seat.Id);
        var order = request.UnitPositionIds ?? [];
        if (order.Count != seats.Count || order.Distinct().Count() != order.Count || !order.All(seats.ContainsKey))
        {
            return Result.Failure<IReadOnlyList<UnitPositionDto>>(Error.Validation("Send every position of the unit once, in the new order."));
        }

        for (var i = 0; i < order.Count; i++)
        {
            seats[order[i]].MoveTo(i);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ListUnitPositionsAsync(tenantId, unitId, cancellationToken);
    }

    public async Task<Result> RemoveUnitPositionAsync(Guid tenantId, Guid unitPositionId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var seat = snapshot.Seats.FirstOrDefault(s => s.Id == unitPositionId);
        if (seat is null)
        {
            return Result.Failure(Error.NotFound("Unit position was not found."));
        }

        if (snapshot.Holdings.Any(h => h.UnitPositionId == unitPositionId))
        {
            return Result.Failure(Error.Conflict("People hold this position. Change their positions first."));
        }

        repository.RemoveUnitPosition(seat);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>After the posts of its kind: a new post sits with the unit's other posts of its position.</summary>
    private static int NextSortOrder(Snapshot snapshot, Guid companyId, Guid? unitId, Guid positionId)
    {
        var siblings = snapshot.Seats.Where(seat => seat.CompanyId == companyId && seat.UnitId == unitId).ToList();
        return siblings.FirstOrDefault(seat => seat.PositionId == positionId)?.SortOrder
            ?? siblings.Select(seat => seat.SortOrder).DefaultIfEmpty(-1).Max() + 1;
    }

    /// <summary>The company's next free number (codes are numbers unless someone wrote their own).</summary>
    private static string NextCode(Snapshot snapshot, Guid companyId, IReadOnlyCollection<string> alsoTaken)
    {
        var taken = snapshot.Seats.Where(seat => seat.CompanyId == companyId).Select(seat => seat.Code).Concat(alsoTaken).ToHashSet();
        var next = taken.Select(code => int.TryParse(code, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        while (taken.Contains(next.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            next++;
        }

        return next.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Error? ValidateCode(Snapshot snapshot, Guid companyId, string code, Guid? exceptSeatId)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > MaxCodeLength)
        {
            return Error.Validation("A post's code is required (at most 40 characters).");
        }

        return snapshot.Seats.Any(seat => seat.CompanyId == companyId && seat.Id != exceptSeatId && string.Equals(seat.Code, code, StringComparison.OrdinalIgnoreCase))
            ? Error.Conflict("Another post of the company has this code.")
            : null;
    }

    // ---------------------------------------------------------------- assignments

    public async Task<Result<IReadOnlyList<UserAssignmentDto>>> ListAssignmentsAsync(
        Guid tenantId, Guid? companyId, Guid? unitId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var resolver = snapshot.Resolver();
        var selected = snapshot.Assignments
            .Where(a => companyId is null || a.CompanyId == companyId)
            .Where(a => unitId is null || a.UnitId == unitId)
            .ToList();
        var people = await PeopleAsync(selected.Select(a => (Guid?)a.UserId).Concat(selected.Select(a => resolver.ManagerOf(a.UserId)?.ManagerUserId)), cancellationToken);
        return Result.Success<IReadOnlyList<UserAssignmentDto>>(selected
            .Select(a => ToDto(a, snapshot, resolver, people))
            .OrderBy(dto => dto.CompanyName).ThenBy(dto => dto.UnitName).ThenBy(dto => dto.DisplayName)
            .ToList());
    }

    public async Task<Result<UserAssignmentDto>> GetAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var assignment = snapshot.Assignments.FirstOrDefault(a => a.UserId == userId);
        if (assignment is null)
        {
            return Result.Failure<UserAssignmentDto>(Error.NotFound("This user has no place in the organization structure yet."));
        }

        return Result.Success(await ToDtoAsync(assignment, snapshot, cancellationToken));
    }

    public async Task<Result<UserAssignmentDto>> SetAssignmentAsync(
        Guid tenantId, Guid userId, SetUserAssignmentRequest request, CancellationToken cancellationToken)
    {
        if (!await IsUserOfTenantAsync(tenantId, userId, cancellationToken))
        {
            return Result.Failure<UserAssignmentDto>(Error.NotFound("User was not found."));
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var company = snapshot.Companies.GetValueOrDefault(request.CompanyId);
        if (company is null)
        {
            return Result.Failure<UserAssignmentDto>(Error.Validation("Company was not found."));
        }

        if (!company.IsActive)
        {
            return Result.Failure<UserAssignmentDto>(Error.Validation("The company is inactive."));
        }

        var resolver = snapshot.Resolver();
        if (request.UnitId is { } unitId)
        {
            if (!snapshot.Units.ContainsKey(unitId))
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("Organization unit was not found."));
            }

            if (resolver.CompanyOfUnit(unitId) != request.CompanyId)
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("The unit does not belong to this company."));
            }
        }

        var current = snapshot.Assignments.FirstOrDefault(a => a.UserId == userId);
        var positionKept = current is not null && current.PositionId == request.PositionId && current.CompanyId == request.CompanyId;
        if (request.PositionId is { } positionId && !positionKept)
        {
            if (snapshot.Positions.GetValueOrDefault(positionId) is not { } position || !position.IsActive)
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("Position was not found or is inactive."));
            }

            if (!snapshot.IsFor(positionId, request.CompanyId))
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("This position is not for the person's company."));
            }
        }

        if (request.ManagerUserId is { } managerId)
        {
            if (managerId == userId)
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("A user cannot be their own manager."));
            }

            if (!await IsUserOfTenantAsync(tenantId, managerId, cancellationToken))
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("The manager was not found in this organization."));
            }
        }

        var holdings = snapshot.Holdings.Where(h => h.UserId == userId).ToList();
        var held = holdings.Select(h => h.UnitPositionId).ToHashSet();
        var byId = snapshot.Seats.ToDictionary(seat => seat.Id);
        List<UnitPosition>? seats = null;
        if (request.UnitPositionIds is { } seatIds)
        {
            if (seatIds.Distinct().Count() != seatIds.Count)
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("A position is listed more than once."));
            }

            if (!seatIds.All(id => byId.TryGetValue(id, out var seat) && snapshot.IsShown(seat)))
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("Unit position was not found."));
            }

            seats = seatIds.Select(id => byId[id]).ToList();

            // A position newly given must be active; one already held may stay.
            if (seats.Any(seat => !held.Contains(seat.Id)
                && snapshot.Positions.GetValueOrDefault(seat.PositionId) is not { IsActive: true }))
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("Position was not found or is inactive."));
            }

            // One person per post.
            if (seats.Any(seat => !held.Contains(seat.Id) && snapshot.HolderOf(seat.Id) is not null))
            {
                return Result.Failure<UserAssignmentDto>(Error.Conflict("Someone already holds this post. Free it first."));
            }
        }

        // The main position: the one asked for, or with a list of positions, the first held in
        // the person's unit (else the first held).
        // Directly in the company (no unit), the main position is the one asked for.
        var mainPositionId = seats is null || (request.UnitId is null && request.PositionId is not null)
            || seats.Any(seat => seat.PositionId == request.PositionId)
            ? request.PositionId
            : (seats.FirstOrDefault(seat => seat.UnitId == request.UnitId) ?? seats.FirstOrDefault())?.PositionId;

        // As older clients set it (no list of posts): the main position, held in the person's unit
        // or directly in the company - a post of it they hold there, else a vacant one, else a new
        // one - in place of the previous main one. Their other posts stay.
        UnitPosition? mainSeat = null;
        UnitPosition? previousMainSeat = null;
        var newMainSeat = false;
        if (seats is null)
        {
            previousMainSeat = current is { PositionId: { } previousPosition }
                ? snapshot.Seats.FirstOrDefault(seat => held.Contains(seat.Id) && seat.CompanyId == current.CompanyId
                    && seat.UnitId == current.UnitId && seat.PositionId == previousPosition)
                : null;
            if (mainPositionId is { } mainPosition)
            {
                var here = snapshot.Seats
                    .Where(seat => seat.CompanyId == request.CompanyId && seat.UnitId == request.UnitId && seat.PositionId == mainPosition)
                    .OrderBy(seat => seat.Code, CodeOrder.Instance)
                    .ToList();
                mainSeat = here.FirstOrDefault(seat => held.Contains(seat.Id)) ?? here.FirstOrDefault(seat => snapshot.HolderOf(seat.Id) is null);
                if (mainSeat is null)
                {
                    mainSeat = new UnitPosition(
                        Guid.NewGuid(), tenantId, request.CompanyId, request.UnitId, mainPosition,
                        NextSortOrder(snapshot, request.CompanyId, request.UnitId, mainPosition), NextCode(snapshot, request.CompanyId, []));
                    newMainSeat = true;
                }
            }
        }

        var finalSeats = seats
            ?? holdings.Where(h => h.UnitPositionId != previousMainSeat?.Id || h.UnitPositionId == mainSeat?.Id)
                .Select(h => byId.GetValueOrDefault(h.UnitPositionId))
                .Append(mainSeat)
                .Where(seat => seat is not null)
                .Select(seat => seat!)
                .DistinctBy(seat => seat.Id)
                .ToList();

        // The company's head: whoever holds its head's post (directly in it, of its position with no
        // superior). One per company; anyone no longer holding it stops being head.
        var headOf = finalSeats.Where(snapshot.IsHeadPost).Select(seat => seat.CompanyId).ToHashSet();
        foreach (var headCompany in headOf)
        {
            if (snapshot.Companies[headCompany].ManagerUserId is { } otherHead && otherHead != userId
                && snapshot.Holdings.Any(h => h.UserId == otherHead && byId.TryGetValue(h.UnitPositionId, out var seat)
                    && seat.CompanyId == headCompany && snapshot.IsHeadPost(seat)))
            {
                return Result.Failure<UserAssignmentDto>(Error.Conflict(
                    "This company already has a head. Take the head's position from them first."));
            }
        }

        if (newMainSeat && headOf.Contains(mainSeat!.CompanyId)
            && snapshot.Seats.Any(seat => seat.CompanyId == mainSeat.CompanyId && snapshot.IsHeadPost(seat)))
        {
            return Result.Failure<UserAssignmentDto>(Error.Conflict(
                "This company already has a head. Take the head's position from them first."));
        }

        var heads = snapshot.Companies.Values
            .Where(c => c.ManagerUserId == userId && !headOf.Contains(c.Id))
            .ToDictionary(c => c.Id, _ => (Guid?)null);
        foreach (var headCompany in headOf.Where(c => snapshot.Companies[c].ManagerUserId != userId))
        {
            heads[headCompany] = userId;
        }

        var after = snapshot.Resolver(
            assignmentOverride: new AssignmentNode(userId, request.CompanyId, request.UnitId, request.ManagerUserId),
            companyManagerOverrides: heads);
        if ((after.ManagementChainOf(userId).Count > 0 && LoopsBackTo(after, userId))
            || (heads.Count > 0 && snapshot.Resolver().FindLoop() is null && after.FindLoop() is not null))
        {
            return Result.Failure<UserAssignmentDto>(Error.Conflict("This change would create a management loop."));
        }

        var assignment = await repository.GetAssignmentAsync(tenantId, userId, cancellationToken);
        if (assignment is null)
        {
            assignment = new UserAssignment(Guid.NewGuid(), tenantId, userId);
            await repository.AddAssignmentAsync(assignment, cancellationToken);
        }

        assignment.Set(request.CompanyId, request.UnitId, mainPositionId, request.ManagerUserId);
        foreach (var (companyId, head) in heads)
        {
            snapshot.Companies[companyId].SetHead(head);
        }

        if (newMainSeat)
        {
            await repository.AddUnitPositionAsync(mainSeat!, cancellationToken);
        }

        var wanted = finalSeats.Select(seat => seat.Id).ToHashSet();
        holdings.Where(h => !wanted.Contains(h.UnitPositionId)).ToList().ForEach(repository.RemoveUserPosition);
        foreach (var seat in finalSeats.Where(seat => !held.Contains(seat.Id)))
        {
            await repository.AddUserPositionAsync(new UserPosition(Guid.NewGuid(), tenantId, userId, seat.Id), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDtoAsync(assignment, await LoadAsync(tenantId, cancellationToken), cancellationToken));
    }

    public async Task<Result> RemoveAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var assignment = await repository.GetAssignmentAsync(tenantId, userId, cancellationToken);
        if (assignment is null)
        {
            return Result.Failure(Error.NotFound("This user has no place in the organization structure."));
        }

        repository.RemoveAssignment(assignment);
        foreach (var company in (await repository.ListCompaniesAsync(tenantId, cancellationToken)).Where(c => c.ManagerUserId == userId))
        {
            company.SetHead(null);
        }

        foreach (var held in (await repository.ListUserPositionsAsync(tenantId, cancellationToken)).Where(h => h.UserId == userId))
        {
            repository.RemoveUserPosition(held);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ---------------------------------------------------------------- hierarchy

    public async Task<Result<ManagerDto?>> GetManagerAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var resolver = (await LoadAsync(tenantId, cancellationToken)).Resolver();
        if (resolver.ManagerOf(userId) is not { } manager)
        {
            return Result.Success<ManagerDto?>(null);
        }

        var people = await PeopleAsync([manager.ManagerUserId], cancellationToken);
        return Result.Success<ManagerDto?>(ToManagerDto(manager, people));
    }

    public async Task<Result<IReadOnlyList<SubordinateDto>>> GetSubordinatesAsync(
        Guid tenantId, Guid userId, bool includeIndirect, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var resolver = snapshot.Resolver();
        var found = includeIndirect
            ? resolver.AllSubordinatesOf(userId)
            : resolver.DirectSubordinatesOf(userId).Select(id => (UserId: id, Depth: 1)).ToList();

        var people = await PeopleAsync(found.Select(s => (Guid?)s.UserId), cancellationToken);
        return Result.Success<IReadOnlyList<SubordinateDto>>(found
            .Select(s =>
            {
                var assignment = resolver.Assignments[s.UserId];
                var person = Person(s.UserId, people);
                return new SubordinateDto(
                    s.UserId,
                    person.DisplayName,
                    person.IsActive,
                    s.Depth,
                    resolver.ManagerOf(s.UserId)!.ManagerUserId,
                    snapshot.Companies.GetValueOrDefault(assignment.CompanyId)?.Name ?? string.Empty,
                    assignment.UnitId is { } u ? snapshot.Units.GetValueOrDefault(u)?.Name : null,
                    PositionTitle(snapshot, s.UserId));
            })
            .OrderBy(dto => dto.Depth).ThenBy(dto => dto.DisplayName)
            .ToList());
    }

    public async Task<Result<MyPlacementDto>> GetPlacementAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var assignment = snapshot.Assignments.FirstOrDefault(a => a.UserId == userId);
        if (assignment is null)
        {
            return Result.Success(new MyPlacementDto(null, []));
        }

        var chain = snapshot.Resolver().ManagementChainOf(userId);
        var people = await PeopleAsync(chain.Select(id => (Guid?)id), cancellationToken);
        return Result.Success(new MyPlacementDto(
            await ToDtoAsync(assignment, snapshot, cancellationToken),
            chain.Select(id => Person(id, people)).ToList()));
    }

    public async Task<Result<IReadOnlyList<OrgChartCompanyDto>>> GetChartAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var resolver = snapshot.Resolver();
        var teams = await workTeams.ListWorkTeamsAsync(tenantId, cancellationToken);
        var people = await PeopleAsync(
            snapshot.Assignments.Select(a => (Guid?)a.UserId)
                .Concat(snapshot.Companies.Values.Select(c => c.ManagerUserId))
                .Concat(snapshot.Units.Values.Select(u => u.ManagerUserId))
                .Concat(teams.SelectMany(t => t.MemberIds.Append(t.OwnerUserId)).Select(id => (Guid?)id))
                .Concat(snapshot.Holdings.Select(h => (Guid?)h.UserId)),
            cancellationToken);

        // Units no longer carry parent references: build an empty children map.
        var children = new Dictionary<Guid, List<OrganizationUnit>>();

        OrgChartMemberDto MemberOf(Guid userId)
        {
            var person = Person(userId, people);
            return new OrgChartMemberDto(
                userId, person.DisplayName, person.IsActive, PositionTitle(snapshot, userId), resolver.ManagerOf(userId)?.ManagerUserId,
                person.AvatarUrl, snapshot.Assignments.FirstOrDefault(a => a.UserId == userId)?.PositionId);
        }

        OrgChartMemberDto Member(UserAssignment a) => MemberOf(a.UserId);

        // A unit: its positions in its order, each with whoever holds it (wherever they sit), and
        // the unit's people who hold none of its positions.
        OrgChartUnitDto BuildUnit(OrganizationUnit unit, HashSet<Guid> visited)
        {
            var unitPeople = snapshot.Assignments.Where(a => a.UnitId == unit.Id).ToList();
            var seats = snapshot.Seats
                .Where(seat => seat.UnitId == unit.Id && snapshot.IsShown(seat))
                .OrderBy(seat => seat.SortOrder).ThenBy(seat => snapshot.Positions[seat.PositionId].Title).ThenBy(seat => seat.Code, CodeOrder.Instance)
                .ToList();
            var seatIds = seats.Select(seat => seat.Id).ToHashSet();
            var holdings = snapshot.Holdings.Where(h => seatIds.Contains(h.UnitPositionId)).ToList();
            var seated = holdings.Select(h => h.UserId).ToHashSet();
            return new OrgChartUnitDto(
                unit.Id,
                unit.Name,
                unit.ManagerUserId is { } m ? Person(m, people) : null,
                unitPeople.Where(a => !seated.Contains(a.UserId)).Select(Member).OrderBy(x => x.DisplayName).ToList(),
                new List<OrgChartUnitDto>(),
                teams.Where(team => UnitOfOwner(snapshot, team) == unit.Id).Select(team => TeamDto(team, snapshot, resolver, people)).ToList(),
                unit.IsActive,
                unitPeople.Select(a => a.UserId).Concat(seated).Distinct().Count(),
                seats.Select(PostOf).ToList());
        }

        OrgChartPositionDto PostOf(UnitPosition seat) => new(
            seat.Id,
            seat.PositionId,
            snapshot.Positions[seat.PositionId].Title,
            seat.SortOrder,
            snapshot.Positions[seat.PositionId].IsActive,
            snapshot.Holdings.Where(h => h.UnitPositionId == seat.Id).Select(h => MemberOf(h.UserId)).ToList(),
            seat.Code,
            snapshot.IsHeadPost(seat));

        var chart = snapshot.Companies.Values
            .Where(company => companyId is null || company.Id == companyId)
            .OrderBy(company => company.Name)
            .Select(company =>
            {
                var visited = new HashSet<Guid>();
                var topUnits = snapshot.Links
                    .Where(link => link.CompanyId == company.Id && snapshot.Units.ContainsKey(link.UnitId))
                    .Select(link => snapshot.Units[link.UnitId])
                    .Where(unit => visited.Add(unit.Id))
                    .OrderBy(unit => unit.Name)
                    .ToList();

                // The posts directly in the company, the head's first.
                var companyPosts = snapshot.Seats
                    .Where(seat => seat.CompanyId == company.Id && seat.UnitId is null && snapshot.IsShown(seat))
                    .OrderBy(seat => !snapshot.IsHeadPost(seat)).ThenBy(seat => seat.SortOrder)
                    .ThenBy(seat => snapshot.Positions[seat.PositionId].Title).ThenBy(seat => seat.Code, CodeOrder.Instance)
                    .ToList();
                var inCompanyPosts = snapshot.Holdings
                    .Where(h => companyPosts.Any(seat => seat.Id == h.UnitPositionId)).Select(h => h.UserId).ToHashSet();

                // People directly under the company (holding none of its own posts), plus anyone whose
                // unit no longer belongs to it (e.g. the unit was moved in Nexus.Organization), so
                // nobody drops off the chart.
                var companyMembers = snapshot.Assignments
                    .Where(a => a.CompanyId == company.Id && a.UserId != company.ManagerUserId && !inCompanyPosts.Contains(a.UserId)
                        && (a.UnitId is null || resolver.CompanyOfUnit(a.UnitId.Value) != company.Id))
                    .Select(Member)
                    .OrderBy(x => x.DisplayName)
                    .ToList();

                return new OrgChartCompanyDto(
                    company.Id,
                    company.Name,
                    company.Code,
                    company.IsActive,
                    company.ManagerUserId is { } m ? Person(m, people) : null,
                    companyMembers,
                    topUnits.Select(unit => BuildUnit(unit, visited)).ToList(),
                    snapshot.Assignments.Count(a => a.CompanyId == company.Id),
                    companyPosts.Select(PostOf).ToList());
            })
            .ToList();

        return Result.Success<IReadOnlyList<OrgChartCompanyDto>>(chart);
    }

    // ---------------------------------------------------------------- work teams

    /// <summary>Work teams whose builder is in no unit of a company, so the chart shows them on their own.</summary>
    public async Task<Result<IReadOnlyList<OrgChartTeamDto>>> ListTeamsOutsideUnitsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var resolver = snapshot.Resolver();
        var linkedUnits = snapshot.Links.Select(link => link.UnitId).ToHashSet();
        var outside = (await workTeams.ListWorkTeamsAsync(tenantId, cancellationToken))
            .Where(team => UnitOfOwner(snapshot, team) is not { } unitId || !linkedUnits.Contains(unitId))
            .ToList();
        var people = await PeopleAsync(outside.SelectMany(t => t.MemberIds.Append(t.OwnerUserId)).Select(id => (Guid?)id), cancellationToken);
        return Result.Success<IReadOnlyList<OrgChartTeamDto>>(outside.Select(team => TeamDto(team, snapshot, resolver, people)).ToList());
    }

    public async Task<Result<IReadOnlyList<UnplacedUserDto>>> ListUnplacedUsersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var placed = (await repository.ListAssignmentsAsync(tenantId, cancellationToken)).Select(a => a.UserId).ToHashSet();
        return Result.Success<IReadOnlyList<UnplacedUserDto>>((await users.GetActiveUsersInTenantAsync(tenantId, cancellationToken))
            .Where(user => !placed.Contains(user.Id))
            .OrderBy(user => user.DisplayName)
            .Select(user => new UnplacedUserDto(user.Id, user.DisplayName, user.AvatarUrl))
            .ToList());
    }

    /// <summary>The unit of the person who built the team; a team shows under it.</summary>
    private static Guid? UnitOfOwner(Snapshot snapshot, WorkTeamInfo team) =>
        snapshot.Assignments.FirstOrDefault(a => a.UserId == team.OwnerUserId)?.UnitId;

    /// <summary>A work team with all its members (placed in the structure or not).</summary>
    private static OrgChartTeamDto TeamDto(WorkTeamInfo team, Snapshot snapshot, HierarchyResolver resolver, IReadOnlyDictionary<Guid, UserContact> people) => new(
        team.Id,
        team.Name,
        team.Description,
        Person(team.OwnerUserId, people),
        team.MemberIds.Distinct()
            .Select(userId =>
            {
                var person = Person(userId, people);
                var assignment = snapshot.Assignments.FirstOrDefault(a => a.UserId == userId);
                return new OrgChartMemberDto(
                    userId, person.DisplayName, person.IsActive, PositionTitle(snapshot, userId), resolver.ManagerOf(userId)?.ManagerUserId,
                    person.AvatarUrl, assignment?.PositionId);
            })
            .OrderBy(member => member.UserId != team.OwnerUserId).ThenBy(member => member.DisplayName)
            .ToList());

    // ---------------------------------------------------------------- helpers

    private static bool LoopsBackTo(HierarchyResolver resolver, Guid userId)
    {
        // ManagementChainOf stops before repeating anyone; the chain loops back to the user when
        // its last manager's own manager is the user.
        var chain = resolver.ManagementChainOf(userId);
        return resolver.ManagerOf(chain[^1])?.ManagerUserId == userId;
    }

    private async Task<bool> IsUserOfTenantAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        (await users.GetUsersAsync([userId], cancellationToken)).Any(user => user.Id == userId && user.TenantId == tenantId);

    private async Task<IReadOnlyDictionary<Guid, UserContact>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, UserContact>();
        }

        return (await users.GetUsersAsync(distinct, cancellationToken)).ToDictionary(user => user.Id);
    }

    private static PersonRef Person(Guid userId, IReadOnlyDictionary<Guid, UserContact> people) =>
        people.TryGetValue(userId, out var user)
            ? new PersonRef(userId, user.DisplayName, user.IsActive, user.AvatarUrl)
            : new PersonRef(userId, "کاربر حذف‌شده", false);

    private static ManagerDto ToManagerDto(ResolvedManager manager, IReadOnlyDictionary<Guid, UserContact> people)
    {
        var person = Person(manager.ManagerUserId, people);
        return new ManagerDto(person.UserId, person.DisplayName, person.IsActive, manager.Source.ToString());
    }

    /// <summary>Every position the person holds, the main one first; or the main one alone.</summary>
    private static string? PositionTitle(Snapshot snapshot, Guid userId)
    {
        var titles = HeldSeats(snapshot, userId).Select(seat => snapshot.Positions[seat.PositionId].Title).Distinct().ToList();
        if (titles.Count > 0)
        {
            return string.Join("، ", titles);
        }

        return snapshot.Assignments.FirstOrDefault(a => a.UserId == userId)?.PositionId is { } positionId
            ? snapshot.Positions.GetValueOrDefault(positionId)?.Title
            : null;
    }

    /// <summary>The unit positions the person holds: the main one, then those of their unit, then the rest.</summary>
    private static IReadOnlyList<UnitPosition> HeldSeats(Snapshot snapshot, Guid userId)
    {
        var assignment = snapshot.Assignments.FirstOrDefault(a => a.UserId == userId);
        var held = snapshot.Holdings.Where(h => h.UserId == userId).Select(h => h.UnitPositionId).ToHashSet();
        return snapshot.Seats
            .Where(seat => held.Contains(seat.Id) && snapshot.IsShown(seat))
            .OrderBy(seat => seat.UnitId == assignment?.UnitId && seat.PositionId == assignment?.PositionId ? 0 : seat.UnitId == assignment?.UnitId ? 1 : 2)
            .ThenBy(seat => snapshot.PlaceOf(seat))
            .ThenBy(seat => seat.SortOrder)
            .ToList();
    }

    private static UnitPositionDto ToDto(UnitPosition seat, Snapshot snapshot, IReadOnlyDictionary<Guid, UserContact> people)
    {
        var position = snapshot.Positions.GetValueOrDefault(seat.PositionId);
        return new UnitPositionDto(
            seat.Id,
            seat.UnitId,
            seat.UnitId is { } unitId ? snapshot.Units.GetValueOrDefault(unitId)?.Name ?? string.Empty : null,
            seat.PositionId,
            position?.Title ?? string.Empty,
            seat.SortOrder,
            position?.IsActive ?? false,
            snapshot.Holdings.Where(h => h.UnitPositionId == seat.Id)
                .Select(h => Person(h.UserId, people)).OrderBy(person => person.DisplayName).ToList(),
            seat.Code,
            seat.CompanyId,
            snapshot.Companies.GetValueOrDefault(seat.CompanyId)?.Name);
    }

    private async Task<CompanyDto> ToDtoAsync(Guid tenantId, Company company, CancellationToken cancellationToken)
    {
        var links = await repository.ListCompanyUnitsAsync(tenantId, cancellationToken);
        var people = await PeopleAsync([company.ManagerUserId], cancellationToken);
        return ToDto(company, links, people);
    }

    private static CompanyDto ToDto(Company company, IReadOnlyList<CompanyUnit> links, IReadOnlyDictionary<Guid, UserContact> people) => new(
        company.Id,
        company.TenantId,
        company.Name,
        company.Code,
        company.Description,
        company.ManagerUserId is { } m ? Person(m, people) : null,
        company.IsActive,
        links.Where(link => link.CompanyId == company.Id).Select(link => link.UnitId).ToList());

    private static CompanyUnitDto ToDto(CompanyUnit link, Snapshot snapshot) => new(
        link.UnitId,
        snapshot.Units.GetValueOrDefault(link.UnitId)?.Name ?? string.Empty,
        link.CompanyId,
        snapshot.Companies.GetValueOrDefault(link.CompanyId)?.Name ?? string.Empty);

    private static PositionDto ToDto(Position position, IReadOnlyList<PositionCompany> links) => new(
        position.Id,
        position.Title,
        position.ParentPositionId,
        position.IsActive,
        links.Where(link => link.PositionId == position.Id).Select(link => link.CompanyId).ToList());

    private async Task<UserAssignmentDto> ToDtoAsync(UserAssignment assignment, Snapshot snapshot, CancellationToken cancellationToken)
    {
        var resolver = snapshot.Resolver();
        var people = await PeopleAsync([assignment.UserId, resolver.ManagerOf(assignment.UserId)?.ManagerUserId], cancellationToken);
        return ToDto(assignment, snapshot, resolver, people);
    }

    private static UserAssignmentDto ToDto(
        UserAssignment assignment, Snapshot snapshot, HierarchyResolver resolver, IReadOnlyDictionary<Guid, UserContact> people)
    {
        var person = Person(assignment.UserId, people);
        var manager = resolver.ManagerOf(assignment.UserId);
        return new UserAssignmentDto(
            assignment.UserId,
            person.DisplayName,
            person.IsActive,
            assignment.CompanyId,
            snapshot.Companies.GetValueOrDefault(assignment.CompanyId)?.Name ?? string.Empty,
            assignment.UnitId,
            assignment.UnitId is { } u ? snapshot.Units.GetValueOrDefault(u)?.Name : null,
            assignment.PositionId,
            assignment.PositionId is { } p ? snapshot.Positions.GetValueOrDefault(p)?.Title : null,
            assignment.ManagerUserId,
            manager is null ? null : ToManagerDto(manager, people),
            HeldSeats(snapshot, assignment.UserId)
                .Select(seat => new HeldPositionDto(
                    seat.Id, seat.UnitId, snapshot.PlaceOf(seat), seat.PositionId, snapshot.Positions[seat.PositionId].Title, seat.Code))
                .ToList());
    }

    /// <summary>Post codes in natural order: numbers by value (2 before 10), then the rest.</summary>
    private sealed class CodeOrder : IComparer<string>
    {
        public static readonly CodeOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            var xn = int.TryParse(x, out var a);
            var yn = int.TryParse(y, out var b);
            return xn && yn ? a.CompareTo(b) : xn ? -1 : yn ? 1 : string.CompareOrdinal(x, y);
        }
    }

    private async Task<Snapshot> LoadAsync(Guid tenantId, CancellationToken cancellationToken) => new(
        (await units.ListAsync(tenantId, cancellationToken)).ToDictionary(unit => unit.Id),
        (await repository.ListCompaniesAsync(tenantId, cancellationToken)).ToDictionary(company => company.Id),
        await repository.ListCompanyUnitsAsync(tenantId, cancellationToken),
        (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToDictionary(position => position.Id),
        await repository.ListAssignmentsAsync(tenantId, cancellationToken),
        await repository.ListUnitPositionsAsync(tenantId, cancellationToken),
        await repository.ListUserPositionsAsync(tenantId, cancellationToken),
        await repository.ListPositionCompaniesAsync(tenantId, cancellationToken));

    /// <summary>
    /// One tenant's structure, read once per request. Seats: the positions units have;
    /// Holdings: who holds which of them; PositionCompanies: which companies each position is for.
    /// </summary>
    private sealed record Snapshot(
        IReadOnlyDictionary<Guid, OrganizationUnit> Units,
        IReadOnlyDictionary<Guid, Company> Companies,
        IReadOnlyList<CompanyUnit> Links,
        IReadOnlyDictionary<Guid, Position> Positions,
        IReadOnlyList<UserAssignment> Assignments,
        IReadOnlyList<UnitPosition> Seats,
        IReadOnlyList<UserPosition> Holdings,
        IReadOnlyList<PositionCompany> PositionCompanies)
    {
        public bool IsFor(Guid positionId, Guid companyId) =>
            PositionCompanies.Any(link => link.PositionId == positionId && link.CompanyId == companyId);

        /// <summary>A post whose unit (if any) and position still exist.</summary>
        public bool IsShown(UnitPosition seat) =>
            (seat.UnitId is not { } unitId || Units.ContainsKey(unitId)) && Positions.ContainsKey(seat.PositionId) && Companies.ContainsKey(seat.CompanyId);

        public Guid? HolderOf(Guid seatId) => Holdings.FirstOrDefault(h => h.UnitPositionId == seatId)?.UserId;

        /// <summary>The company head's post: directly in the company, of its position with no superior.</summary>
        public bool IsHeadPost(UnitPosition seat) =>
            seat.UnitId is null && Positions.GetValueOrDefault(seat.PositionId) is { ParentPositionId: null } && IsFor(seat.PositionId, seat.CompanyId);

        /// <summary>Where a post is: its unit's name, or its company's for a post directly in it.</summary>
        public string PlaceOf(UnitPosition seat) => seat.UnitId is { } unitId
            ? Units.GetValueOrDefault(unitId)?.Name ?? string.Empty
            : Companies.GetValueOrDefault(seat.CompanyId)?.Name ?? string.Empty;

        public HashSet<Guid> CompaniesOf(Guid positionId) =>
            PositionCompanies.Where(link => link.PositionId == positionId).Select(link => link.CompanyId).ToHashSet();

        public HierarchyResolver Resolver(
            AssignmentNode? assignmentOverride = null,
            IReadOnlyDictionary<Guid, Guid?>? companyManagerOverrides = null)
        {
            var assignments = Assignments
                .Where(a => assignmentOverride is null || a.UserId != assignmentOverride.UserId)
                .Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId))
                .ToList();
            if (assignmentOverride is not null)
            {
                assignments.Add(assignmentOverride);
            }

            var companyManagers = Companies.Values.ToDictionary(
                company => company.Id,
                company => companyManagerOverrides is not null && companyManagerOverrides.TryGetValue(company.Id, out var head)
                    ? head
                    : company.ManagerUserId);

            return new HierarchyResolver(
                Units.Values.Select(unit => new UnitNode(unit.Id, null, unit.ManagerUserId)),
                Links.ToDictionary(link => link.UnitId, link => link.CompanyId),
                companyManagers,
                assignments);
        }
    }
}
