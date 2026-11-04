using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Nexus.OrgStructure.Application.Dtos;
using NexusCore.Application.Common;

namespace Nexus.CompositionTests;

/// <summary>
/// Bodies the API reads as the web does (camelCase): a misspelt or missing field of these requests
/// is refused with its name, never ignored; the bodies the screens send still read as before.
/// </summary>
public sealed class OrgRequestBodyTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string Refused<T>(string body)
    {
        var json = Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<T>(body, Web));
        return SafeErrorResponses.DescribeBadBody(new BadHttpRequestException("bad", 400, json));
    }

    [Fact]
    public void AMisspeltField_IsNamed()
    {
        Assert.Contains("«style»", Refused<CreateLinkTypeRequest>("""{"name":"x","style":"solid","countsAsManager":true}"""));
        Assert.Contains("«newParentNodeId»", Refused<MoveNodeRequest>("""{"newParentNodeId":"00000000-0000-0000-0000-000000000001"}"""));
    }

    [Fact]
    public void AMissingField_IsNamed_AndAnEmptyMoveIsNotTakenAsToTheTop()
    {
        Assert.Contains("parentNodeId", Refused<MoveNodeRequest>("{}"));
        Assert.Contains("targetNodeId", Refused<MergeNodeRequest>("""{"effectiveDate":null}"""));
    }

    [Fact]
    public void AnArrayInsteadOfTheObject_SaysWhatIsExpected() =>
        Assert.Contains("شیء JSON", Refused<SetAutoLinkRulesRequest>("[]"));

    [Fact]
    public void WhatTheScreensSend_StillReads()
    {
        Assert.Null(JsonSerializer.Deserialize<MoveNodeRequest>("""{"parentNodeId":null,"rowVersion":"AAA="}""", Web)!.ParentNodeId);
        Assert.Equal("solid", JsonSerializer.Deserialize<UpdateLinkTypeRequest>("""{"name":"x","code":"c","lineStyle":"solid","countsAsManager":true,"isActive":true}""", Web)!.LineStyle);
        Assert.Equal("dotted", JsonSerializer.Deserialize<CreateLinkTypeRequest>("""{"name":"x","code":null,"lineStyle":"dotted","countsAsManager":false}""", Web)!.LineStyle);
    }
}
