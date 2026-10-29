using Nexus.OrgStructure.Domain;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Which post is a company's head's. A post directly in the company is the head's when it is
/// marked (<see cref="UnitPosition.IsCompanyHead"/>) or - until all data carries the mark - when
/// its position has no superior. Both ways are accepted together, so marked and not yet marked
/// data give the same head.
/// </summary>
public static class CompanyHeadPost
{
    /// <param name="positionIsForCompany">Whether the post's position is one of the company's (true when not checked).</param>
    public static bool Is(UnitPosition seat, Position? position, bool positionIsForCompany = true) =>
        seat.UnitId is null && (seat.IsCompanyHead || (position is { ParentPositionId: null } && positionIsForCompany));
}
