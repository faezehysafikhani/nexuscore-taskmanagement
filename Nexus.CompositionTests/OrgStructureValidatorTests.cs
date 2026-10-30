using Nexus.OrgStructure.Application;

namespace Nexus.CompositionTests;

/// <summary>M3: the rules of a sound structure, with no database.</summary>
public sealed class OrgStructureValidatorTests
{
    private static StructureNodeInput N(string code, string? parent, string? type = null, bool linked = false, int? row = null) =>
        new(code, "نام " + code, parent, type, linked, row);

    private static List<StructureErrorKind> Kinds(IEnumerable<StructureError> errors) => errors.Select(error => error.Kind).ToList();

    [Fact]
    public void ASoundStructure_HasNoErrors()
    {
        var errors = OrgStructureValidator.Validate(
            [N("A", null, "board", linked: true), N("B", "A", "deputy"), N("C", "B", "branch")],
            ["board", "deputy", "branch"],
            [new(null, "board"), new("board", "deputy"), new("deputy", "branch")]);
        Assert.Empty(errors);
    }

    [Fact]
    public void AMissingParent_IsReported_WithItsRow()
    {
        var errors = OrgStructureValidator.Validate([N("A", null, row: 2), N("B", "ZZ", row: 3)]);
        var error = Assert.Single(errors);
        Assert.Equal(StructureErrorKind.MissingParent, error.Kind);
        Assert.Equal("B", error.NodeCode);
        Assert.Equal(3, error.Row);
        Assert.Contains("سطر 3", error.Message);
    }

    [Fact]
    public void ADuplicateCode_IsReported_OnTheLaterOne_IgnoringCaseAndSpaces()
    {
        var errors = OrgStructureValidator.Validate([N("a1", null, row: 2), N(" A1 ", null, row: 5)]);
        var error = Assert.Single(errors);
        Assert.Equal(StructureErrorKind.DuplicateCode, error.Kind);
        Assert.Equal(5, error.Row);
        Assert.Contains("سطر 2", error.Message);
    }

    [Fact]
    public void AnEmptyCode_IsReported()
    {
        var error = Assert.Single(OrgStructureValidator.Validate([N(" ", null, row: 4)]));
        Assert.Equal(StructureErrorKind.MissingCode, error.Kind);
        Assert.Equal(4, error.Row);
    }

    [Fact]
    public void ALoop_IsReportedOnEachOfItsRows_WithThePath_AndNodesLeadingIntoItAreNot()
    {
        var errors = OrgStructureValidator.Validate([N("A", "C", row: 2), N("B", "A", row: 3), N("C", "B", row: 4), N("D", "C", row: 5)]);
        Assert.Equal([2, 3, 4], errors.Select(error => error.Row!.Value));
        Assert.All(errors, error => Assert.Equal(StructureErrorKind.Cycle, error.Kind));
        Assert.All(errors, error => Assert.Contains("A ← B ← C ← A", error.Message));
    }

    [Fact]
    public void AType_IsNamedAsPeopleKnowIt_WithItsCode()
    {
        var rules = new StructureTypeRule[] { new(null, "board") };
        var errors = OrgStructureValidator.Validate([N("D", null, "deputy", row: 2)], ["board", "deputy"], rules,
            new Dictionary<string, string> { ["deputy"] = "معاونت", ["board"] = "هیأت مدیره" });
        Assert.Contains("«معاونت» (deputy)", Assert.Single(errors).Message);
    }

    [Fact]
    public void ANodeUnderItself_IsALoop()
    {
        var error = Assert.Single(OrgStructureValidator.Validate([N("A", "A")]));
        Assert.Equal(StructureErrorKind.Cycle, error.Kind);
    }

    [Fact]
    public void TwoSeparateLoops_AreTwoErrors()
    {
        var errors = OrgStructureValidator.Validate([N("A", "B"), N("B", "A"), N("X", "Y"), N("Y", "X")]);
        Assert.Equal([StructureErrorKind.Cycle, StructureErrorKind.Cycle, StructureErrorKind.Cycle, StructureErrorKind.Cycle], Kinds(errors));
    }

    [Fact]
    public void WhatUnderWhat_ChecksTheRule_AndTheTop()
    {
        var rules = new StructureTypeRule[] { new(null, "board"), new("board", "deputy") };
        string[] types = ["board", "deputy", "branch"];

        var wrongChild = OrgStructureValidator.Validate([N("A", null, "board"), N("B", "A", "branch", row: 3)], types, rules);
        var error = Assert.Single(wrongChild);
        Assert.Equal(StructureErrorKind.TypeNotAllowedHere, error.Kind);
        Assert.Equal("B", error.NodeCode);

        var wrongTop = OrgStructureValidator.Validate([N("D", null, "deputy", row: 2)], types, rules);
        Assert.Equal(StructureErrorKind.TypeNotAllowedHere, Assert.Single(wrongTop).Kind);
        Assert.Contains("بالاترین سطح", wrongTop[0].Message);
    }

    [Fact]
    public void WithNoRules_OrNoTypeOnTheNode_NothingIsRestricted()
    {
        Assert.Empty(OrgStructureValidator.Validate([N("A", null, "branch"), N("B", "A", "board")], ["board", "branch"], []));
        Assert.Empty(OrgStructureValidator.Validate([N("A", null, "board"), N("B", "A"), N("C", "B", "branch")], ["board", "branch"], [new(null, "board"), new("board", "branch")]));
    }

    [Fact]
    public void AnUnknownType_IsReported_AndNotAlsoAWrongPlace()
    {
        var errors = OrgStructureValidator.Validate([N("A", null, "ghost")], ["board"], [new(null, "board")]);
        Assert.Equal([StructureErrorKind.UnknownType], Kinds(errors));
    }

    [Fact]
    public void ACompanyLink_IsAllowedOnlyOnATopNode()
    {
        Assert.Empty(OrgStructureValidator.Validate([N("A", null, linked: true), N("B", null, linked: true)]));

        var error = Assert.Single(OrgStructureValidator.Validate([N("A", null), N("B", "A", linked: true, row: 3)]));
        Assert.Equal(StructureErrorKind.LinkNotOnTopNode, error.Kind);
        Assert.Equal(3, error.Row);
    }

    [Fact]
    public void EveryFaultIsListed_InRowOrder()
    {
        var errors = OrgStructureValidator.Validate([N("A", "ZZ", row: 2), N("A", null, row: 3), N("B", "C", row: 4), N("C", "B", row: 5), N("D", "A", linked: true, row: 6)]);
        Assert.Equal([StructureErrorKind.MissingParent, StructureErrorKind.DuplicateCode, StructureErrorKind.Cycle, StructureErrorKind.Cycle, StructureErrorKind.LinkNotOnTopNode], Kinds(errors));
    }
}
