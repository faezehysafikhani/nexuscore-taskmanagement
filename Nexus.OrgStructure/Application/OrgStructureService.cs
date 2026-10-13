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

    Task<Result<IReadOnlyList<UserAssignmentDto>>> ListAssignmentsAsync(Guid tenantId, Guid? companyId, Guid? unitId, CancellationToken cancellationToken);
    Task<Result<UserAssignmentDto>> GetAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<Result<UserAssignmentDto>> SetAssignmentAsync(Guid tenantId, Guid userId, SetUserAssignmentRequest request, CancellationToken cancellationToken);
    Task<Result> RemoveAssignmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<Result<ManagerDto?>> GetManagerAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<SubordinateDto>>> GetSubordinatesAsync(Guid tenantId, Guid userId, bool includeIndirect, CancellationToken cancellationToken);
    Task<Result<MyPlacementDto>> GetPlacementAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<OrgChartCompanyDto>>> GetChartAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken);
}

public sealed class OrgStructureService(
    IOrgStructureRepository repository,
    IOrgStructureUnitOfWork unitOfWork,
    IOrganizationUnitRepository units,
    IUserDirectory users) : IOrgStructureService
{
    private const int MaxNameLength = 200;
    private const int MaxTitleLength = 120;
    private const int MaxDescriptionLength = 1000;

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
        var invalid = await ValidateCompanyAsync(tenantId, null, request.Name, request.Code, request.Description, request.ManagerUserId, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<CompanyDto>(invalid);
        }

        var company = new Company(Guid.NewGuid(), tenantId, request.Name, request.Code, request.Description, request.ManagerUserId);
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

        var invalid = await ValidateCompanyAsync(tenantId, id, request.Name, request.Code, request.Description, request.ManagerUserId, cancellationToken);
        if (invalid is not null)
        {
            return Result.Failure<CompanyDto>(invalid);
        }

        if (company.ManagerUserId != request.ManagerUserId)
        {
            var snapshot = await LoadAsync(tenantId, cancellationToken);
            var before = snapshot.Resolver();
            var after = snapshot.Resolver(companyManagerOverride: (id, request.ManagerUserId));
            if (before.FindLoop() is null && after.FindLoop() is not null)
            {
                return Result.Failure<CompanyDto>(Error.Conflict("This change would create a management loop."));
            }
        }

        company.Update(request.Name, request.Code, request.Description, request.ManagerUserId, request.IsActive);
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
            || (await repository.ListAssignmentsAsync(tenantId, cancellationToken)).Any(assignment => assignment.CompanyId == id);
        if (inUse)
        {
            return Result.Failure(Error.Conflict("The company still has units or people. Move them first, or deactivate the company instead."));
        }

        repository.RemoveCompany(company);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ValidateCompanyAsync(
        Guid tenantId, Guid? id, string? name, string? code, string? description, Guid? managerUserId, CancellationToken cancellationToken)
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

        if (managerUserId is { } managerId && !await IsUserOfTenantAsync(tenantId, managerId, cancellationToken))
        {
            return Error.Validation("The company manager was not found in this organization.");
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

        repository.RemoveCompanyUnit(link);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ---------------------------------------------------------------- positions

    public async Task<Result<IReadOnlyList<PositionDto>>> ListPositionsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Result.Success<IReadOnlyList<PositionDto>>((await repository.ListPositionsAsync(tenantId, cancellationToken)).Select(ToDto).ToList());

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

        var position = new Position(Guid.NewGuid(), tenantId, request.Title, request.ParentPositionId);
        await repository.AddPositionAsync(position, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(position));
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

        position.Update(request.Title, request.ParentPositionId, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(position));
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

        if ((await repository.ListAssignmentsAsync(tenantId, cancellationToken)).Any(a => a.PositionId == id))
        {
            return Result.Failure(Error.Conflict("People hold this position. Change their position first, or deactivate it instead."));
        }

        repository.RemovePosition(position);
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

        if (request.PositionId is { } positionId
            && (snapshot.Positions.GetValueOrDefault(positionId) is not { } position || !position.IsActive))
        {
            return Result.Failure<UserAssignmentDto>(Error.Validation("Position was not found or is inactive."));
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

        var after = snapshot.Resolver(assignmentOverride: new AssignmentNode(userId, request.CompanyId, request.UnitId, request.ManagerUserId));
        if (after.ManagementChainOf(userId).Count > 0 && LoopsBackTo(after, userId))
        {
            return Result.Failure<UserAssignmentDto>(Error.Conflict("This change would create a management loop."));
        }

        var assignment = await repository.GetAssignmentAsync(tenantId, userId, cancellationToken);
        if (assignment is null)
        {
            assignment = new UserAssignment(Guid.NewGuid(), tenantId, userId);
            await repository.AddAssignmentAsync(assignment, cancellationToken);
        }

        assignment.Set(request.CompanyId, request.UnitId, request.PositionId, request.ManagerUserId);
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
        var people = await PeopleAsync(
            snapshot.Assignments.Select(a => (Guid?)a.UserId)
                .Concat(snapshot.Companies.Values.Select(c => c.ManagerUserId))
                .Concat(snapshot.Units.Values.Select(u => u.ManagerUserId)),
            cancellationToken);

        // Units no longer carry parent references: build an empty children map.
        var children = new Dictionary<Guid, List<OrganizationUnit>>();

        OrgChartMemberDto Member(UserAssignment a)
        {
            var person = Person(a.UserId, people);
            return new OrgChartMemberDto(a.UserId, person.DisplayName, person.IsActive, PositionTitle(snapshot, a.UserId), resolver.ManagerOf(a.UserId)?.ManagerUserId);
        }

        OrgChartUnitDto BuildUnit(OrganizationUnit unit, HashSet<Guid> visited) => new(
            unit.Id,
            unit.Name,
            unit.ManagerUserId is { } m ? Person(m, people) : null,
            snapshot.Assignments.Where(a => a.UnitId == unit.Id).Select(Member).OrderBy(x => x.DisplayName).ToList(),
            new List<OrgChartUnitDto>());

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

                // People directly under the company, plus anyone whose unit no longer belongs to
                // it (e.g. the unit was moved in Nexus.Organization), so nobody drops off the chart.
                var companyMembers = snapshot.Assignments
                    .Where(a => a.CompanyId == company.Id && (a.UnitId is null || resolver.CompanyOfUnit(a.UnitId.Value) != company.Id))
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
                    topUnits.Select(unit => BuildUnit(unit, visited)).ToList());
            })
            .ToList();

        return Result.Success<IReadOnlyList<OrgChartCompanyDto>>(chart);
    }

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
            ? new PersonRef(userId, user.DisplayName, user.IsActive)
            : new PersonRef(userId, "کاربر حذف‌شده", false);

    private static ManagerDto ToManagerDto(ResolvedManager manager, IReadOnlyDictionary<Guid, UserContact> people)
    {
        var person = Person(manager.ManagerUserId, people);
        return new ManagerDto(person.UserId, person.DisplayName, person.IsActive, manager.Source.ToString());
    }

    private static string? PositionTitle(Snapshot snapshot, Guid userId) =>
        snapshot.Assignments.FirstOrDefault(a => a.UserId == userId)?.PositionId is { } positionId
            ? snapshot.Positions.GetValueOrDefault(positionId)?.Title
            : null;

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

    private static PositionDto ToDto(Position position) => new(position.Id, position.Title, position.ParentPositionId, position.IsActive);

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
            manager is null ? null : ToManagerDto(manager, people));
    }

    private async Task<Snapshot> LoadAsync(Guid tenantId, CancellationToken cancellationToken) => new(
        (await units.ListAsync(tenantId, cancellationToken)).ToDictionary(unit => unit.Id),
        (await repository.ListCompaniesAsync(tenantId, cancellationToken)).ToDictionary(company => company.Id),
        await repository.ListCompanyUnitsAsync(tenantId, cancellationToken),
        (await repository.ListPositionsAsync(tenantId, cancellationToken)).ToDictionary(position => position.Id),
        await repository.ListAssignmentsAsync(tenantId, cancellationToken));

    /// <summary>One tenant's structure, read once per request.</summary>
    private sealed record Snapshot(
        IReadOnlyDictionary<Guid, OrganizationUnit> Units,
        IReadOnlyDictionary<Guid, Company> Companies,
        IReadOnlyList<CompanyUnit> Links,
        IReadOnlyDictionary<Guid, Position> Positions,
        IReadOnlyList<UserAssignment> Assignments)
    {
        public HierarchyResolver Resolver(
            AssignmentNode? assignmentOverride = null,
            (Guid CompanyId, Guid? ManagerUserId)? companyManagerOverride = null)
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
                company => companyManagerOverride is { } o && o.CompanyId == company.Id ? o.ManagerUserId : company.ManagerUserId);

            return new HierarchyResolver(
                Units.Values.Select(unit => new UnitNode(unit.Id, null, unit.ManagerUserId)),
                Links.ToDictionary(link => link.UnitId, link => link.CompanyId),
                companyManagers,
                assignments);
        }
    }
}
