namespace Nexus.OrgStructure.Application;

/// <summary>
/// The three kinds of the simple organization chart, in one place (the UI has the same table in
/// <c>orgKinds.ts</c>; a test keeps them equal):
/// <list type="bullet">
/// <item><b>org</b> («سازمان») - the company itself, the one root; nobody sits in it. Not a node.</item>
/// <item><b>unit</b> («واحد سازمانی», e.g. a deputy) - no person; may have a head (one of its direct positions).</item>
/// <item><b>position</b> («سمت / پست سازمانی») - one or more people; only positions sit under it.</item>
/// </list>
/// A kind is a node type with a fixed code. The kind of a node cannot change after it is made.
/// </summary>
public static class OrgNodeKinds
{
    public const string Unit = "unit";
    public const string Position = "position";

    public const string UnitTypeCode = "kind_unit";
    public const string PositionTypeCode = "kind_position";

    // Codes the first version of the simple chart made (org_post / org_role): the same two kinds.
    private const string LegacyUnitTypeCode = "org_post";
    private const string LegacyPositionTypeCode = "org_role";

    public const string UnitName = "واحد سازمانی";
    public const string PositionName = "سمت / پست سازمانی";

    /// <summary>The kind of a node type code; null for any other (older, custom) type.</summary>
    public static string? KindOfTypeCode(string? typeCode) => typeCode switch
    {
        UnitTypeCode or LegacyUnitTypeCode => Unit,
        PositionTypeCode or LegacyPositionTypeCode => Position,
        _ => null,
    };

    public static bool IsKindTypeCode(string? typeCode) => KindOfTypeCode(typeCode) is not null;

    /// <summary>Whether a node of kind <paramref name="child"/> may sit under <paramref name="parent"/> (null: directly under the organization).</summary>
    public static bool CanContain(string? parent, string child) => parent switch
    {
        null => child is Unit or Position,
        Unit => child is Unit or Position,
        Position => child == Position,
        _ => false,
    };

    /// <summary>Only a position holds people.</summary>
    public static bool HoldsPeople(string? kind) => kind == Position;

    /// <summary>Only a unit has a head.</summary>
    public static bool CanHaveHead(string? kind) => kind == Unit;

    /// <summary>Every allowed (parent kind, child kind) pair; a null parent is the organization.</summary>
    public static IReadOnlyList<(string? Parent, string Child)> Pairs { get; } =
    [
        (null, Unit), (null, Position),
        (Unit, Unit), (Unit, Position),
        (Position, Position),
    ];
}
