using Nexus.Organization.Application;
using Nexus.Organization.Domain;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Domain;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
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
    Task<Result<UnitPositionDto>> SetPostParentAsync(Guid tenantId, Guid unitPositionId, SetPostParentRequest request, CancellationToken cancellationToken);
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

    /// <summary>Units of other companies, which a user kept to their own company does not see (empty when not kept).</summary>
    Task<Result<IReadOnlyList<Guid>>> ListOtherCompaniesUnitsAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed partial class OrgStructureService(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    IOrganizationUnitRepository units,
    IUserDirectory users,
    IWorkTeamDirectory workTeams,
    TaskPostDirectory? taskPosts = null,
    CompanyReachScope? companyReach = null,
    ICurrentUserContext? currentUser = null,
    IOrgTemplateService? templates = null,
    TimeProvider? clock = null,
    IOrgStructureWriteLock? writeLock = null,
    NexusCore.Application.Files.IFileStorage? files = null) : IOrgStructureService
{
    private const int MaxNameLength = 200;
    private const int MaxTitleLength = 120;
    private const int MaxDescriptionLength = 1000;
    private const int MaxCodeLength = 40;

    // ---------------------------------------------------------------- companies

    public async Task<Result<IReadOnlyList<CompanyDto>>> ListCompaniesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        var companies = (await repository.ListCompaniesAsync(tenantId, cancellationToken)).Where(c => mine is null || mine.Contains(c.Id)).ToList();
        var links = await repository.ListCompanyUnitsAsync(tenantId, cancellationToken);
        var people = await PeopleAsync(companies.Select(c => c.ManagerUserId), cancellationToken);
        return Result.Success<IReadOnlyList<CompanyDto>>(companies.Select(c => ToDto(c, links, people)).ToList());
    }

    public async Task<Result<CompanyDto>> GetCompanyAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(tenantId, id, cancellationToken);
        if (company is null || !await IsMineAsync(tenantId, company.Id, cancellationToken))
        {
            return Result.Failure<CompanyDto>(Error.NotFound("شرکت پیدا نشد."));
        }

        return Result.Success(await ToDtoAsync(tenantId, company, cancellationToken));
    }

    public async Task<Result<CompanyDto>> CreateCompanyAsync(Guid tenantId, CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        // A new company is another company: not for someone kept to their own.
        if (await MyCompaniesAsync(tenantId, cancellationToken) is not null)
        {
            return Result.Failure<CompanyDto>(OtherCompany());
        }

        // No code given: the next free automatic one.
        var code = string.IsNullOrWhiteSpace(request.Code) ? await NextCompanyCodeAsync(tenantId, cancellationToken) : request.Code;
        var invalid = await ValidateCompanyAsync(tenantId, null, request.Name, code, request.Description, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<CompanyDto>(invalid);
        }

        if (request.Details is { } details && DetailsProblem(details) is { } detailsProblem)
        {
            return Result.Failure<CompanyDto>(detailsProblem);
        }

        var unitIds = request.UnitIds ?? [];
        if (unitIds.Count > 0 && NewCompanyUnitsProblem(await LoadAsync(tenantId, cancellationToken), unitIds) is { } unitsProblem)
        {
            return Result.Failure<CompanyDto>(unitsProblem);
        }

        // No head yet: whoever is given the company's position with no superior becomes it.
        var company = new Company(Guid.NewGuid(), tenantId, request.Name, code, request.Description, managerUserId: null);
        if (request.Details is { } given)
        {
            Apply(company, given);
        }
        if (request.ParentCompanyId is { } parentCompanyId)
        {
            if (await repository.GetCompanyAsync(tenantId, parentCompanyId, cancellationToken) is null)
            {
                return Result.Failure<CompanyDto>(Error.Validation("سازمان بالادستی پیدا نشد."));
            }

            company.SetGroup(parentCompanyId, company.TemplateId);
        }

        if (request.TemplateId is { } templateId)
        {
            if (templates is null)
            {
                return Result.Failure<CompanyDto>(Error.Validation("ساخت شرکت از قالب در دسترس نیست."));
            }

            var made = await templates.CreateCompanyFromTemplateAsync(tenantId, company, templateId, cancellationToken);
            if (!made.IsSuccess)
            {
                return Result.Failure<CompanyDto>(made.Error);
            }

            // Its posts: the company's own and each node's (from its type's pattern).
            await MakeTemplateCompanyPostsAsync(tenantId, company.Id, made.Value!, cancellationToken);
            if (unitIds.Count > 0)
            {
                await LinkNewCompanyUnitsAsync(tenantId, company.Id, unitIds, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success(await ToDtoAsync(tenantId, company, cancellationToken));
        }

        await repository.AddCompanyAsync(company, cancellationToken);
        await LinkNewCompanyUnitsAsync(tenantId, company.Id, unitIds, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDtoAsync(tenantId, company, cancellationToken));
    }

    public async Task<Result<CompanyDto>> UpdateCompanyAsync(Guid tenantId, Guid id, UpdateCompanyRequest request, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(tenantId, id, cancellationToken);
        if (company is null || !await IsMineAsync(tenantId, company.Id, cancellationToken))
        {
            return Result.Failure<CompanyDto>(Error.NotFound("شرکت پیدا نشد."));
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
        if (company is null || !await IsMineAsync(tenantId, company.Id, cancellationToken))
        {
            return Result.Failure(Error.NotFound("شرکت پیدا نشد."));
        }

        var inUse = (await repository.ListCompanyUnitsAsync(tenantId, cancellationToken)).Any(link => link.CompanyId == id)
            || (await repository.ListAssignmentsAsync(tenantId, cancellationToken)).Any(assignment => assignment.CompanyId == id)
            || (await repository.ListUnitPositionsAsync(tenantId, cancellationToken)).Any(seat => seat.CompanyId == id);
        if (inUse)
        {
            return Result.Failure(Error.Conflict("شرکت هنوز واحد یا فرد دارد؛ اول آن‌ها را جابه‌جا کنید، یا به‌جای حذف، شرکت را غیرفعال کنید."));
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
            return Error.Validation("نام و کد را وارد کنید.");
        }

        if (name.Trim().Length > MaxNameLength || code.Trim().Length > 40 || (description?.Trim().Length ?? 0) > MaxDescriptionLength)
        {
            return Error.Validation("نام، کد یا توضیحات بیش از حد بلند است.");
        }

        if (await repository.CompanyCodeExistsAsync(tenantId, code.Trim(), id, cancellationToken))
        {
            return Error.Conflict("شرکتی با این کد از قبل هست.");
        }

        return null;
    }

    // ---------------------------------------------------------------- company units

    public async Task<Result<IReadOnlyList<CompanyUnitDto>>> ListCompanyUnitsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<CompanyUnitDto>>(snapshot.Links
            .Where(link => mine is null || mine.Contains(link.CompanyId))
            .Select(link => ToDto(link, snapshot))
            .OrderBy(dto => dto.CompanyName).ThenBy(dto => dto.UnitName)
            .ToList());
    }

    public async Task<Result<CompanyUnitDto>> LinkUnitAsync(Guid tenantId, Guid unitId, LinkUnitRequest request, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (!snapshot.Units.ContainsKey(unitId))
        {
            return Result.Failure<CompanyUnitDto>(Error.NotFound("واحد سازمانی پیدا نشد."));
        }

        // Only the top unit of a branch is linked: one under another belongs to that one's company.
        if (snapshot.Graph.ParentOf(unitId) is not null)
        {
            return Result.Failure<CompanyUnitDto>(Error.Validation(
                "Only the top unit of a branch is linked to a company; this unit belongs to the company of the unit above it."));
        }

        var company = snapshot.Companies.GetValueOrDefault(request.CompanyId);
        if (company is null || !await IsMineAsync(tenantId, company.Id, cancellationToken))
        {
            return Result.Failure<CompanyUnitDto>(Error.NotFound("شرکت پیدا نشد."));
        }

        // A unit of another company stays with it.
        if (snapshot.Links.FirstOrDefault(link => link.UnitId == unitId) is { } linked
            && !await IsMineAsync(tenantId, linked.CompanyId, cancellationToken))
        {
            return Result.Failure<CompanyUnitDto>(OtherCompany());
        }

        if (!company.IsActive)
        {
            return Result.Failure<CompanyUnitDto>(Error.Validation("شرکت غیرفعال است."));
        }

        var resolver = snapshot.Resolver();

        var subtree = resolver.SubtreeOf(unitId);
        if (snapshot.Links.Any(link => link.UnitId != unitId && subtree.Contains(link.UnitId)))
        {
            return Result.Failure<CompanyUnitDto>(Error.Validation("واحدهای زیر این واحد به شرکت وصل‌اند؛ اول آن‌ها را جدا کنید."));
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
        if (link is null || !await IsMineAsync(tenantId, link.CompanyId, cancellationToken))
        {
            return Result.Failure(Error.NotFound("این واحد به شرکتی وصل نیست."));
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var subtree = snapshot.Resolver().SubtreeOf(unitId);
        if (snapshot.Assignments.Any(a => a.UnitId is { } u && subtree.Contains(u)))
        {
            return Result.Failure(Error.Conflict("به این واحد یا واحدهای زیرش فرد داده شده است؛ اول آن‌ها را جابه‌جا کنید."));
        }

        if (snapshot.Seats.Any(seat => seat.UnitId is { } u && subtree.Contains(u)))
        {
            return Result.Failure(Error.Conflict("واحد پست دارد؛ اول آن‌ها را بردارید."));
        }

        repository.RemoveCompanyUnit(link);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ---------------------------------------------------------------- positions

    public async Task<Result<IReadOnlyList<PositionDto>>> ListPositionsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var links = await repository.ListPositionCompaniesAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        return Result.Success<IReadOnlyList<PositionDto>>(
            (await repository.ListPositionsAsync(tenantId, cancellationToken))
                .Where(position => mine is null || links.All(link => link.PositionId != position.Id)
                    || links.Any(link => link.PositionId == position.Id && mine.Contains(link.CompanyId)))
                .Select(position => ToDto(position, links)).ToList());
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
            return Result.Failure<PositionDto>(Error.Validation("سمت بالاتر پیدا نشد."));
        }

        var companyIds = (request.CompanyIds ?? []).Distinct().ToList();
        if (await MyCompaniesAsync(tenantId, cancellationToken) is { } mine && !companyIds.All(mine.Contains))
        {
            return Result.Failure<PositionDto>(OtherCompany());
        }

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
            return Result.Failure<PositionDto>(Error.NotFound("سمت پیدا نشد."));
        }

        var invalid = await ValidatePositionAsync(tenantId, id, request.Title, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<PositionDto>(invalid);
        }

        if (request.ParentPositionId == id)
        {
            return Result.Failure<PositionDto>(Error.Validation("سمت را نمی‌شود زیر خودش گذاشت."));
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (request.ParentPositionId is { } parentId && !snapshot.Positions.ContainsKey(parentId))
        {
            return Result.Failure<PositionDto>(Error.Validation("سمت بالاتر پیدا نشد."));
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
                    return Result.Failure<PositionDto>(Error.Conflict("این تغییر در سلسله‌مراتب سمت‌ها حلقه می‌سازد."));
                }

                var parent = snapshot.Positions.GetValueOrDefault(current)?.ParentPositionId;
                if (parent is null) break;
                current = parent.Value;
            }
        }

        // Without a list (older clients) the position keeps its companies.
        var currentCompanies = snapshot.CompaniesOf(id);
        var companyIds = request.CompanyIds is null ? currentCompanies.ToList() : request.CompanyIds.Distinct().ToList();

        // Kept to one's own company: only a position of one's own companies alone, and only for them.
        if (await MyCompaniesAsync(tenantId, cancellationToken) is { } mine
            && (currentCompanies.Count == 0 || !currentCompanies.All(mine.Contains) || !companyIds.All(mine.Contains)))
        {
            return Result.Failure<PositionDto>(OtherCompany());
        }
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
            return Error.Validation("برای سمت دست‌کم یک شرکت انتخاب کنید.");
        }

        if (!companyIds.All(snapshot.Companies.ContainsKey))
        {
            return Error.Validation("شرکت پیدا نشد.");
        }

        if (parentId is { } parent && !companyIds.All(companyId => snapshot.IsFor(parent, companyId)))
        {
            return Error.Validation("سمت مافوق باید برای همه‌ی شرکت‌های این سمت باشد.");
        }

        if (parentId is null && checkRootIn.Any(companyId => snapshot.PositionCompanies.Any(link =>
                link.CompanyId == companyId && link.PositionId != positionId
                && snapshot.Positions.GetValueOrDefault(link.PositionId) is { ParentPositionId: null })))
        {
            return Error.Conflict("این شرکت از قبل سمتی بدون مافوق (سمت رئیس کل) دارد؛ برای این سمت مافوق انتخاب کنید.");
        }

        return null;
    }

    public async Task<Result> DeletePositionAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var position = await repository.GetPositionAsync(tenantId, id, cancellationToken);
        if (position is null)
        {
            return Result.Failure(Error.NotFound("سمت پیدا نشد."));
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (await MyCompaniesAsync(tenantId, cancellationToken) is { } mine
            && (snapshot.CompaniesOf(id) is not { Count: > 0 } companies || !companies.All(mine.Contains)))
        {
            return Result.Failure(OtherCompany());
        }

        if (snapshot.Positions.Values.Any(p => p.ParentPositionId == id))
        {
            return Result.Failure(Error.Conflict("این سمت زیرمجموعه دارد؛ اول آن‌ها را به سمت دیگری بدهید یا بردارید."));
        }

        if (snapshot.Assignments.Any(a => a.PositionId == id))
        {
            return Result.Failure(Error.Conflict("افرادی این سمت را دارند؛ اول سمتشان را عوض کنید، یا به‌جای حذف، آن را غیرفعال کنید."));
        }

        if (snapshot.Seats.Any(seat => seat.PositionId == id))
        {
            return Result.Failure(Error.Conflict("واحدهایی این سمت را دارند؛ اول از آن‌ها بردارید، یا به‌جای حذف، آن را غیرفعال کنید."));
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
            return Error.Validation("عنوان را وارد کنید.");
        }

        if (title.Trim().Length > MaxTitleLength)
        {
            return Error.Validation("عنوان بیش از حد بلند است.");
        }

        return await repository.PositionTitleExistsAsync(tenantId, title.Trim(), id, cancellationToken)
            ? Error.Conflict("سمتی با این عنوان از قبل هست.")
            : null;
    }

    // ---------------------------------------------------------------- posts (unit positions)

    /// <summary>The posts of one unit (or every post, those directly in companies too), in order, with their holder.</summary>
    public async Task<Result<IReadOnlyList<UnitPositionDto>>> ListUnitPositionsAsync(
        Guid tenantId, Guid? unitId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        var seats = snapshot.Seats
            .Where(seat => (unitId is null || seat.UnitId == unitId) && snapshot.IsShown(seat) && (mine is null || mine.Contains(seat.CompanyId)))
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
            return Result.Failure<UnitPositionDto>(Error.NotFound("واحد سازمانی پیدا نشد."));
        }

        if (snapshot.Resolver().CompanyOfUnit(unitId) is not { } unitCompany)
        {
            return Result.Failure<UnitPositionDto>(Error.Validation("اول واحد را به یک شرکت وصل کنید."));
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
            return Result.Failure<UnitPositionDto>(Error.NotFound("شرکت پیدا نشد."));
        }

        // The head's post: one per company.
        if (snapshot.Positions.GetValueOrDefault(request.PositionId) is { ParentPositionId: null }
            && snapshot.Seats.Any(seat => seat.CompanyId == companyId && snapshot.IsHeadPost(seat)))
        {
            return Result.Failure<UnitPositionDto>(Error.Conflict("شرکت از قبل پست رئیس کل را دارد."));
        }

        return await AddPostAsync(tenantId, snapshot, companyId, null, request, cancellationToken);
    }

    private async Task<Result<UnitPositionDto>> AddPostAsync(
        Guid tenantId, Snapshot snapshot, Guid companyId, Guid? unitId, AddUnitPositionRequest request, CancellationToken cancellationToken)
    {
        if (!await IsMineAsync(tenantId, companyId, cancellationToken))
        {
            return Result.Failure<UnitPositionDto>(unitId is null ? Error.NotFound("شرکت پیدا نشد.") : OtherCompany());
        }

        if (snapshot.Positions.GetValueOrDefault(request.PositionId) is not { IsActive: true })
        {
            return Result.Failure<UnitPositionDto>(Error.Validation("سمت پیدا نشد یا غیرفعال است."));
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

        if (request.ParentPostId is { } parentId)
        {
            if (snapshot.Seats.FirstOrDefault(seat => seat.Id == parentId) is not { } parent)
            {
                return Result.Failure<UnitPositionDto>(Error.Validation("پست بالاتر پیدا نشد."));
            }

            if (parent.CompanyId != companyId || parent.UnitId != unitId)
            {
                return Result.Failure<UnitPositionDto>(Error.Validation("پست بالاتر باید در همان جا باشد (همان واحد، یا مستقیم در شرکت)."));
            }
        }

        var added = new UnitPosition(Guid.NewGuid(), tenantId, companyId, unitId, request.PositionId, NextSortOrder(snapshot, companyId, unitId, request.PositionId), code);
        // No post above given: a new post of a node goes under the node's manager post, if it has one (existing posts are never changed).
        added.PlaceUnder(request.ParentPostId ?? (unitId is { } node ? ManagerPostOf(snapshot.Seats, node)?.Id : null));
        added.SetCompanyHead(CompanyHeadPost.Is(added, snapshot.Positions.GetValueOrDefault(added.PositionId), snapshot.IsFor(added.PositionId, companyId)));
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
        if (seat is not null && !await IsMineAsync(tenantId, seat.CompanyId, cancellationToken))
        {
            seat = null;
        }
        if (seat is null)
        {
            return Result.Failure<UnitPositionDto>(Error.NotFound("پست پیدا نشد."));
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

    /// <summary>
    /// Puts a post under another of its place (same unit, or both directly in the company), or
    /// under nobody: the holder of the post above manages its holder (a vacant one is passed over).
    /// </summary>
    public async Task<Result<UnitPositionDto>> SetPostParentAsync(
        Guid tenantId, Guid unitPositionId, SetPostParentRequest request, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var seat = snapshot.Seats.FirstOrDefault(s => s.Id == unitPositionId);
        if (seat is not null && !await IsMineAsync(tenantId, seat.CompanyId, cancellationToken))
        {
            seat = null;
        }
        if (seat is null)
        {
            return Result.Failure<UnitPositionDto>(Error.NotFound("پست پیدا نشد."));
        }

        if (request.ParentPostId is { } parentId)
        {
            if (snapshot.Seats.FirstOrDefault(s => s.Id == parentId) is not { } parent)
            {
                return Result.Failure<UnitPositionDto>(Error.Validation("پست بالاتر پیدا نشد."));
            }

            if (parent.CompanyId != seat.CompanyId || parent.UnitId != seat.UnitId)
            {
                return Result.Failure<UnitPositionDto>(Error.Validation("پست بالاتر باید در همان جا باشد (همان واحد، یا مستقیم در شرکت)."));
            }

            // Not itself, nor a post below it.
            var visited = new HashSet<Guid>();
            for (Guid? current = parentId; current is { } id && visited.Add(id); current = snapshot.Seats.FirstOrDefault(s => s.Id == id)?.ParentPostId)
            {
                if (id == seat.Id)
                {
                    return Result.Failure<UnitPositionDto>(Error.Conflict("پست را نمی‌شود زیر خودش یا زیر پستی که زیر اوست گذاشت."));
                }
            }
        }

        var after = snapshot.Resolver(parentOverride: (seat.Id, request.ParentPostId));
        if (snapshot.Resolver().FindLoop() is null && after.FindLoop() is not null)
        {
            return Result.Failure<UnitPositionDto>(Error.Conflict("این تغییر حلقه‌ی مدیریتی می‌سازد."));
        }

        seat.PlaceUnder(request.ParentPostId);
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
            return Result.Failure<IReadOnlyList<UnitPositionDto>>(Error.NotFound("واحد سازمانی پیدا نشد."));
        }

        if (snapshot.Resolver().CompanyOfUnit(unitId) is { } unitCompany && !await IsMineAsync(tenantId, unitCompany, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<UnitPositionDto>>(Error.NotFound("واحد سازمانی پیدا نشد."));
        }

        var seats = snapshot.Seats.Where(seat => seat.UnitId == unitId).ToDictionary(seat => seat.Id);
        var order = request.UnitPositionIds ?? [];
        if (order.Count != seats.Count || order.Distinct().Count() != order.Count || !order.All(seats.ContainsKey))
        {
            return Result.Failure<IReadOnlyList<UnitPositionDto>>(Error.Validation("همه‌ی پست‌های واحد را یک‌بار و به ترتیب تازه بفرستید."));
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
        if (seat is not null && !await IsMineAsync(tenantId, seat.CompanyId, cancellationToken))
        {
            seat = null;
        }
        if (seat is null)
        {
            return Result.Failure(Error.NotFound("پست پیدا نشد."));
        }

        if (snapshot.Holdings.Any(h => h.UnitPositionId == unitPositionId))
        {
            return Result.Failure(Error.Conflict("افرادی این پست را دارند؛ اول پست آن‌ها را عوض کنید."));
        }

        if (snapshot.Seats.Any(other => other.ParentPostId == unitPositionId))
        {
            return Result.Failure(Error.Conflict("پست‌هایی زیر این پست‌اند؛ اول آن‌ها را جابه‌جا کنید."));
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
            return Error.Validation("کد پست لازم است (حداکثر ۴۰ نویسه).");
        }

        return snapshot.Seats.Any(seat => seat.CompanyId == companyId && seat.Id != exceptSeatId && string.Equals(seat.Code, code, StringComparison.OrdinalIgnoreCase))
            ? Error.Conflict("پست دیگری از شرکت این کد را دارد.")
            : null;
    }

    // ---------------------------------------------------------------- assignments

    public async Task<Result<IReadOnlyList<UserAssignmentDto>>> ListAssignmentsAsync(
        Guid tenantId, Guid? companyId, Guid? unitId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var resolver = snapshot.Resolver();
        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        var selected = snapshot.Assignments
            .Where(a => mine is null || mine.Contains(a.CompanyId))
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
        if (assignment is null || !await IsMineAsync(tenantId, assignment.CompanyId, cancellationToken))
        {
            return Result.Failure<UserAssignmentDto>(Error.NotFound("این کاربر هنوز جایگاهی در ساختار سازمانی ندارد."));
        }

        return Result.Success(await ToDtoAsync(assignment, snapshot, cancellationToken));
    }

    public async Task<Result<UserAssignmentDto>> SetAssignmentAsync(
        Guid tenantId, Guid userId, SetUserAssignmentRequest request, CancellationToken cancellationToken)
    {
        var appointmentStart = request.StartDate ?? Now;
        if (appointmentStart > Now)
            return Result.Failure<UserAssignmentDto>(Error.Validation("تاریخ شروع جایگاه باید امروز یا قبل از امروز باشد؛ انتصاب آینده از ابزار انتصاب پست ثبت می‌شود."));
        if (request.EndDate is { } endDate && endDate <= appointmentStart)
            return Result.Failure<UserAssignmentDto>(Error.Validation("تاریخ پایان باید بعد از تاریخ شروع باشد."));

        if (!await IsUserOfTenantAsync(tenantId, userId, cancellationToken))
        {
            return Result.Failure<UserAssignmentDto>(Error.NotFound("کاربر پیدا نشد."));
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var company = snapshot.Companies.GetValueOrDefault(request.CompanyId);
        if (company is null || !await IsMineAsync(tenantId, company.Id, cancellationToken))
        {
            return Result.Failure<UserAssignmentDto>(Error.Validation("شرکت پیدا نشد."));
        }

        // Kept to one's own company: its people (or those with no place yet), and a manager from it.
        if (snapshot.Assignments.FirstOrDefault(a => a.UserId == userId) is { } placed
            && !await IsMineAsync(tenantId, placed.CompanyId, cancellationToken))
        {
            return Result.Failure<UserAssignmentDto>(OtherCompany());
        }

        if (request.ManagerUserId is { } chosenManager && currentUser?.UserId is { } me && companyReach is not null
            && await companyReach.GetReachAsync(tenantId, me, cancellationToken) is { } reach && !reach.Contains(chosenManager))
        {
            return Result.Failure<UserAssignmentDto>(OtherCompany());
        }

        if (!company.IsActive)
        {
            return Result.Failure<UserAssignmentDto>(Error.Validation("شرکت غیرفعال است."));
        }

        var resolver = snapshot.Resolver();
        if (request.UnitId is { } unitId)
        {
            if (!snapshot.Units.ContainsKey(unitId))
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("واحد سازمانی پیدا نشد."));
            }

            if (resolver.CompanyOfUnit(unitId) != request.CompanyId)
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("این واحد جزو این شرکت نیست."));
            }
        }

        var current = snapshot.Assignments.FirstOrDefault(a => a.UserId == userId);
        var positionKept = current is not null && current.PositionId == request.PositionId && current.CompanyId == request.CompanyId;
        if (request.PositionId is { } positionId && !positionKept)
        {
            if (snapshot.Positions.GetValueOrDefault(positionId) is not { } position || !position.IsActive)
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("سمت پیدا نشد یا غیرفعال است."));
            }

            if (!snapshot.IsFor(positionId, request.CompanyId))
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("این سمت برای شرکت این فرد نیست."));
            }
        }

        if (request.ManagerUserId is { } managerId)
        {
            if (managerId == userId)
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("کاربر نمی‌تواند مدیر خودش باشد."));
            }

            if (!await IsUserOfTenantAsync(tenantId, managerId, cancellationToken))
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("مدیر در این سازمان پیدا نشد."));
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
                return Result.Failure<UserAssignmentDto>(Error.Validation("یک سمت بیش از یک بار آمده است."));
            }

            if (!seatIds.All(id => byId.TryGetValue(id, out var seat) && snapshot.IsShown(seat)))
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("پست پیدا نشد."));
            }

            seats = seatIds.Select(id => byId[id]).ToList();

            // A position newly given must be active; one already held may stay.
            if (seats.Any(seat => !held.Contains(seat.Id)
                && snapshot.Positions.GetValueOrDefault(seat.PositionId) is not { IsActive: true }))
            {
                return Result.Failure<UserAssignmentDto>(Error.Validation("سمت پیدا نشد یا غیرفعال است."));
            }

            // One person per post.
            if (seats.Any(seat => !held.Contains(seat.Id) && snapshot.HolderOf(seat.Id) is not null))
            {
                return Result.Failure<UserAssignmentDto>(Error.Conflict("کسی این پست را دارد؛ اول آن را خالی کنید."));
            }

            // Nor a post someone is already appointed to from a later day.
            var newSeats = seats.Where(seat => !held.Contains(seat.Id)).Select(seat => seat.Id).ToHashSet();
            if (newSeats.Count > 0 && (await UpcomingAppointmentsAsync(tenantId, cancellationToken)).Any(a => newSeats.Contains(a.UnitPositionId)))
            {
                return Result.Failure<UserAssignmentDto>(Error.Conflict("این پست برای کس دیگری از تاریخی بعد انتصاب دارد."));
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
                    mainSeat.SetCompanyHead(CompanyHeadPost.Is(mainSeat, snapshot.Positions.GetValueOrDefault(mainPosition), snapshot.IsFor(mainPosition, request.CompanyId)));
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
            companyManagerOverrides: heads,
            heldOverride: (mainPositionId, finalSeats));
        if ((after.ManagementChainOf(userId).Count > 0 && LoopsBackTo(after, userId))
            || (snapshot.Resolver().FindLoop() is null && after.FindLoop() is not null))
        {
            return Result.Failure<UserAssignmentDto>(Error.Conflict("این تغییر حلقه‌ی مدیریتی می‌سازد."));
        }

        // A dated request is a new appointment, never an edit of an old history row.
        if ((request.StartDate is not null || request.EndDate is not null) && mainSeat is not null && held.Contains(mainSeat.Id))
            return Result.Failure<UserAssignmentDto>(Error.Conflict("این فرد همین حالا این پست را دارد؛ برای انتصاب مجدد ابتدا انتصاب فعلی را پایان دهید."));
        foreach (var seat in finalSeats.Where(seat => !held.Contains(seat.Id)))
        {
            var past = await repository.ListUserPositionHistoryAsync(tenantId, null, seat.Id, cancellationToken);
            if (past.Any(a => (a.EndDate is null || a.EndDate > appointmentStart)
                && (request.EndDate is null || a.StartDate is null || a.StartDate < request.EndDate)))
                return Result.Failure<UserAssignmentDto>(Error.Conflict("بازه انتصاب با سابقه این پست هم‌پوشانی دارد."));
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
        foreach (var old in holdings.Where(h => !wanted.Contains(h.UnitPositionId)))
        {
            // Keep the old appointment until the replacement starts; never delete its history.
            if (request.StartDate is not null && (old.StartDate is null || old.StartDate <= appointmentStart)) old.End(appointmentStart);
            else repository.RemoveUserPosition(old);
        }
        foreach (var seat in finalSeats.Where(seat => !held.Contains(seat.Id)))
        {
            var appointment = new UserPosition(Guid.NewGuid(), tenantId, userId, seat.Id, appointmentStart);
            if (request.EndDate is { } end) appointment.End(end);
            await repository.AddUserPositionAsync(appointment, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Tasks given to the posts that changed hands move to their new holders.
        if (taskPosts is not null)
        {
            await taskPosts.SyncHoldersAsync(tenantId, held.Except(wanted).Concat(wanted.Except(held)).ToList(), cancellationToken);
        }

        return Result.Success(await ToDtoAsync(assignment, await LoadAsync(tenantId, cancellationToken), cancellationToken));
    }

    public async Task<Result> RemoveAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var assignment = await repository.GetAssignmentAsync(tenantId, userId, cancellationToken);
        if (assignment is null || !await IsMineAsync(tenantId, assignment.CompanyId, cancellationToken))
        {
            return Result.Failure(Error.NotFound("این کاربر جایگاهی در ساختار سازمانی ندارد."));
        }

        repository.RemoveAssignment(assignment);
        foreach (var company in (await repository.ListCompaniesAsync(tenantId, cancellationToken)).Where(c => c.ManagerUserId == userId))
        {
            company.SetHead(null);
        }

        var freed = (await repository.ListUserPositionsAsync(tenantId, cancellationToken)).Where(h => h.UserId == userId).ToList();
        freed.ForEach(repository.RemoveUserPosition);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (taskPosts is not null)
        {
            await taskPosts.SyncHoldersAsync(tenantId, freed.Select(h => h.UnitPositionId).ToList(), cancellationToken);
        }

        return Result.Success();
    }

    // ---------------------------------------------------------------- hierarchy

    public async Task<Result<ManagerDto?>> GetManagerAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await CanReachAsync(tenantId, userId, cancellationToken))
        {
            return Result.Failure<ManagerDto?>(Error.NotFound("کاربر پیدا نشد."));
        }

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
        if (!await CanReachAsync(tenantId, userId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<SubordinateDto>>(Error.NotFound("کاربر پیدا نشد."));
        }

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

    public Task<Result<IReadOnlyList<OrgChartCompanyDto>>> GetChartAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken) =>
        GetChartAsync(tenantId, companyId, null, cancellationToken);

    /// <summary>The chart; with asOf, only the nodes that exist on that day (their ValidFrom/ValidTo). Without it: as always.</summary>
    public async Task<Result<IReadOnlyList<OrgChartCompanyDto>>> GetChartAsync(Guid tenantId, Guid? companyId, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(tenantId, cancellationToken);
        if (asOf is { } date)
        {
            var graph = OrgGraph.AsOf(date, snapshot.Units.Values, await repository.ListNodeTypesAsync(tenantId, cancellationToken), snapshot.Links, snapshot.Seats, snapshot.Holdings);
            var kept = graph.Nodes.Select(node => node.Id).ToHashSet();
            snapshot = snapshot with
            {
                Units = snapshot.Units.Where(pair => kept.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value),
                Graph = graph,
            };
        }

        var resolver = snapshot.Resolver();
        var teams = await workTeams.ListWorkTeamsAsync(tenantId, cancellationToken);
        var people = await PeopleAsync(
            snapshot.Assignments.Select(a => (Guid?)a.UserId)
                .Concat(snapshot.Companies.Values.Select(c => c.ManagerUserId))
                .Concat(snapshot.Graph.Nodes.Select(node => node.ManagerUserId))
                .Concat(teams.SelectMany(t => t.MemberIds.Append(t.OwnerUserId)).Select(id => (Guid?)id))
                .Concat(snapshot.Holdings.Select(h => (Guid?)h.UserId)),
            cancellationToken);

        // Each node's type and secondary links (the dynamic structure; absent for older data).
        var nodeTypes = (await repository.ListNodeTypesAsync(tenantId, cancellationToken)).ToDictionary(type => type.Id);
        var linkTypes = (await repository.ListLinkTypesAsync(tenantId, cancellationToken)).ToDictionary(type => type.Id);
        var linksFrom = (await repository.ListOrgLinksAsync(tenantId, cancellationToken))
            .Where(link => linkTypes.ContainsKey(link.LinkTypeId))
            .ToLookup(link => link.FromUnitId);

        // The nodes under a node, in their order.
        IEnumerable<OrganizationUnit> ChildrenOf(Guid unitId) => snapshot.Graph.ChildrenOf(unitId)
            .Select(id => snapshot.Units.GetValueOrDefault(id)).OfType<OrganizationUnit>()
            .OrderBy(unit => unit.SortOrder).ThenBy(unit => unit.Name);

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
                snapshot.Graph.Node(unit.Id)?.ManagerUserId is { } m ? Person(m, people) : null,
                unitPeople.Where(a => !seated.Contains(a.UserId)).Select(Member).OrderBy(x => x.DisplayName).ToList(),
                ChildrenOf(unit.Id).Where(child => visited.Add(child.Id)).ToList().Select(child => BuildUnit(child, visited)).ToList(),
                teams.Where(team => UnitOfOwner(snapshot, team) == unit.Id).Select(team => TeamDto(team, snapshot, resolver, people)).ToList(),
                unit.IsActive,
                unitPeople.Select(a => a.UserId).Concat(seated).Distinct().Count(),
                seats.Select(PostOf).ToList(),
                snapshot.Graph.ParentOf(unit.Id),
                unit.NodeTypeId is { } typeId && nodeTypes.TryGetValue(typeId, out var nodeType)
                    ? new OrgChartNodeTypeDto(nodeType.Id, nodeType.Name, nodeType.Color, nodeType.Icon, nodeType.IsDomainBoundary)
                    : null,
                linksFrom[unit.Id].Where(link => snapshot.Units.ContainsKey(link.ToUnitId))
                    .Select(link => new OrgChartLinkDto(link.Id, link.ToUnitId, link.LinkTypeId, linkTypes[link.LinkTypeId].Name,
                        linkTypes[link.LinkTypeId].LineStyle, linkTypes[link.LinkTypeId].CountsAsManager))
                    .ToList());
        }

        OrgChartPositionDto PostOf(UnitPosition seat) => new(
            seat.Id,
            seat.PositionId,
            snapshot.Positions[seat.PositionId].Title,
            seat.SortOrder,
            snapshot.Positions[seat.PositionId].IsActive,
            snapshot.Holdings.Where(h => h.UnitPositionId == seat.Id).Select(h => MemberOf(h.UserId)).ToList(),
            seat.Code,
            snapshot.IsHeadPost(seat),
            seat.ParentPostId);

        var mine = await MyCompaniesAsync(tenantId, cancellationToken);
        var chart = snapshot.Companies.Values
            .Where(company => mine is null || mine.Contains(company.Id))
            .Where(company => companyId is null || company.Id == companyId)
            .OrderBy(company => company.Name)
            .Select(company =>
            {
                var visited = new HashSet<Guid>();
                var topUnits = snapshot.Links
                    .Where(link => link.CompanyId == company.Id && snapshot.Units.ContainsKey(link.UnitId))
                    .Select(link => snapshot.Units[link.UnitId])
                    .Where(unit => visited.Add(unit.Id))
                    .OrderBy(unit => unit.SortOrder).ThenBy(unit => unit.Name)
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
                    companyPosts.Select(PostOf).ToList(),
                    company.UiLevel.ToString().ToLowerInvariant(),
                    company.HeadTitle,
                    LogoUrlOf(company));
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
        var reach = companyReach is not null && currentUser?.UserId is { } me
            ? await companyReach.GetReachAsync(tenantId, me, cancellationToken)
            : null;
        var outside = (await workTeams.ListWorkTeamsAsync(tenantId, cancellationToken))
            .Where(team => UnitOfOwner(snapshot, team) is not { } unitId || !linkedUnits.Contains(unitId))
            .Where(team => reach is null || reach.Contains(team.OwnerUserId))
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

    public async Task<Result<IReadOnlyList<Guid>>> ListOtherCompaniesUnitsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await MyCompaniesAsync(tenantId, cancellationToken) is not { } mine)
        {
            return Result.Success<IReadOnlyList<Guid>>([]);
        }

        var snapshot = await LoadAsync(tenantId, cancellationToken);
        var resolver = snapshot.Resolver();
        return Result.Success<IReadOnlyList<Guid>>(snapshot.Units.Keys
            .Where(unitId => resolver.CompanyOfUnit(unitId) is { } company && !mine.Contains(company))
            .ToList());
    }

    // ---------------------------------------------------------------- own company only

    /// <summary>
    /// The companies the signed-in user sees and changes, or null when not kept to their own
    /// (OrgStructure:LimitToOwnCompany off, AllCompanies held, or no signed-in user).
    /// </summary>
    private async Task<IReadOnlySet<Guid>?> MyCompaniesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        companyReach is not null && currentUser?.UserId is { } me
            ? await companyReach.GetCompaniesAsync(tenantId, me, cancellationToken)
            : null;

    private async Task<bool> IsMineAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken) =>
        await MyCompaniesAsync(tenantId, cancellationToken) is not { } mine || mine.Contains(companyId);

    private async Task<bool> CanReachAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        companyReach is null || currentUser?.UserId is not { } me
        || await companyReach.GetReachAsync(tenantId, me, cancellationToken) is not { } reach || reach.Contains(userId);

    private static Error OtherCompany() => Error.Forbidden("فقط شرکت خودتان را می‌توانید ببینید و تغییر دهید.");

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
            snapshot.Companies.GetValueOrDefault(seat.CompanyId)?.Name,
            seat.ParentPostId,
            seat.IsManagerPost);
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
        links.Where(link => link.CompanyId == company.Id).Select(link => link.UnitId).ToList(),
        company.ParentCompanyId,
        company.Phone,
        company.Email,
        company.Website,
        company.NationalId,
        company.EconomicCode,
        company.Address,
        company.HeadTitle,
        LogoUrlOf(company),
        company.UiLevel.ToString().ToLowerInvariant());

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

    private async Task<Snapshot> LoadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var unitList = await units.ListAsync(tenantId, cancellationToken);
        var links = await repository.ListCompanyUnitsAsync(tenantId, cancellationToken);
        var seats = await repository.ListUnitPositionsAsync(tenantId, cancellationToken);
        var holdings = await repository.ListUserPositionsAsync(tenantId, cancellationToken);
        return new(
            unitList.ToDictionary(unit => unit.Id),
            (await repository.ListCompaniesAsync(tenantId, cancellationToken)).ToDictionary(company => company.Id),
            links,
            (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToDictionary(position => position.Id),
            await repository.ListAssignmentsAsync(tenantId, cancellationToken),
            seats,
            holdings,
            await repository.ListPositionCompaniesAsync(tenantId, cancellationToken),
            OrgGraph.Build(unitList, await repository.ListNodeTypesAsync(tenantId, cancellationToken), links, seats, holdings));
    }

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
        IReadOnlyList<PositionCompany> PositionCompanies,
        OrgGraph Graph)
    {
        public bool IsFor(Guid positionId, Guid companyId) =>
            PositionCompanies.Any(link => link.PositionId == positionId && link.CompanyId == companyId);

        /// <summary>A post whose unit (if any) and position still exist.</summary>
        public bool IsShown(UnitPosition seat) =>
            (seat.UnitId is not { } unitId || Units.ContainsKey(unitId)) && Positions.ContainsKey(seat.PositionId) && Companies.ContainsKey(seat.CompanyId);

        public Guid? HolderOf(Guid seatId) => Holdings.FirstOrDefault(h => h.UnitPositionId == seatId)?.UserId;

        /// <summary>The company head's post: directly in the company, marked as the head's or (until all data is marked) of its position with no superior.</summary>
        public bool IsHeadPost(UnitPosition seat) =>
            CompanyHeadPost.Is(seat, Positions.GetValueOrDefault(seat.PositionId), IsFor(seat.PositionId, seat.CompanyId));

        /// <summary>Where a post is: its unit's name, or its company's for a post directly in it.</summary>
        public string PlaceOf(UnitPosition seat) => seat.UnitId is { } unitId
            ? Units.GetValueOrDefault(unitId)?.Name ?? string.Empty
            : Companies.GetValueOrDefault(seat.CompanyId)?.Name ?? string.Empty;

        public HashSet<Guid> CompaniesOf(Guid positionId) =>
            PositionCompanies.Where(link => link.PositionId == positionId).Select(link => link.CompanyId).ToHashSet();

        /// <summary>
        /// The reporting structure; the overrides show it as it would be after a change: a person's
        /// new place (with the main position and posts they would hold), new company heads, or a
        /// post's new parent.
        /// </summary>
        public HierarchyResolver Resolver(
            AssignmentNode? assignmentOverride = null,
            IReadOnlyDictionary<Guid, Guid?>? companyManagerOverrides = null,
            (Guid? PositionId, IReadOnlyList<UnitPosition> Posts)? heldOverride = null,
            (Guid PostId, Guid? ParentPostId)? parentOverride = null)
        {
            var placements = Assignments
                .Where(a => assignmentOverride is null || a.UserId != assignmentOverride.UserId)
                .Select(a => new PostSuperiors.Placement(a.UserId, a.CompanyId, a.UnitId, a.PositionId))
                .ToList();
            var seats = Seats.ToList();
            var holdings = Holdings.ToList();
            if (assignmentOverride is not null)
            {
                placements.Add(new PostSuperiors.Placement(
                    assignmentOverride.UserId, assignmentOverride.CompanyId, assignmentOverride.UnitId,
                    heldOverride?.PositionId ?? Assignments.FirstOrDefault(a => a.UserId == assignmentOverride.UserId)?.PositionId));
                if (heldOverride is { } held)
                {
                    holdings = holdings.Where(h => h.UserId != assignmentOverride.UserId)
                        .Concat(held.Posts.Select(post => new UserPosition(Guid.NewGuid(), Guid.Empty, assignmentOverride.UserId, post.Id)))
                        .ToList();
                    seats.AddRange(held.Posts.Where(post => seats.All(seat => seat.Id != post.Id)));
                }
            }

            var superiors = PostSuperiors.Of(placements, seats, holdings,
                parentOverride is { } moved ? seat => seat.Id == moved.PostId ? moved.ParentPostId : seat.ParentPostId : null);
            var assignments = Assignments
                .Where(a => assignmentOverride is null || a.UserId != assignmentOverride.UserId)
                .Select(a => new AssignmentNode(a.UserId, a.CompanyId, a.UnitId, a.ManagerUserId,
                    superiors.TryGetValue(a.UserId, out var superior) ? superior : null))
                .ToList();
            if (assignmentOverride is not null)
            {
                assignments.Add(assignmentOverride with
                {
                    PostSuperiorUserId = superiors.TryGetValue(assignmentOverride.UserId, out var superior) ? superior : null,
                });
            }

            var companyManagers = Companies.Values.ToDictionary(
                company => company.Id,
                company => companyManagerOverrides is not null && companyManagerOverrides.TryGetValue(company.Id, out var head)
                    ? head
                    : company.ManagerUserId);

            return new HierarchyResolver(Graph, companyManagers, assignments);
        }
    }
}
