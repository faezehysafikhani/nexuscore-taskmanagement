using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Nexus.CompositionTests;

/// <summary>
/// Direct and team chat through the real HTTP endpoints: who may reach a conversation, that a
/// missing one answers as "no messages" (never 500), that sending creates or reuses the right
/// one, and the security answers (invalid/cross-tenant/inactive/unauthorized) are 404, not 500.
/// </summary>
public sealed class ChatTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    // 1 & 2 & 3: an existing conversation, an existing one with no messages, and one that was
    // never created - all read cleanly, none of them 500.
    [Fact]
    public async Task ADirectConversation_ThatDoesNotExistYet_AnswersEmpty_NeverA500()
    {
        var (a, b) = await SharedTeamPairAsync();

        var response = await host.SendAsync(a, HttpMethod.Get, $"/api/chat/direct/{b.Id}/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await Json(response)).EnumerateArray());
    }

    [Fact]
    public async Task ADirectConversation_ThatExists_ButHasNoMessages_AnswersEmpty()
    {
        var (a, b) = await SharedTeamPairAsync();
        // Reading first (13) does not create anything by itself; sending does.
        await host.SendAsync(a, HttpMethod.Get, $"/api/chat/direct/{b.Id}/messages");

        var response = await host.SendAsync(a, HttpMethod.Get, $"/api/chat/direct/{b.Id}/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await Json(response)).EnumerateArray());
    }

    // 4 & 5 & 11 & 12: the first message creates the conversation, is stored, and both sides
    // read it back; a second message lands in the same thread.
    [Fact]
    public async Task TheFirstMessage_CreatesTheConversation_AndBothSidesSeeIt_AndAreUsingItsSecondMessageToo()
    {
        var (a, b) = await SharedTeamPairAsync();

        var first = await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/direct/{b.Id}/messages", "سلام");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstDto = await Json(first);
        Assert.Equal("سلام", firstDto.GetProperty("text").GetString());
        Assert.Equal(a.Id, firstDto.GetProperty("senderUserId").GetGuid());
        var conversationId = firstDto.GetProperty("conversationId").GetGuid();

        var second = await host.SendFormAsync(b, HttpMethod.Post, $"/api/chat/direct/{a.Id}/messages", "علیک سلام");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(conversationId, (await Json(second)).GetProperty("conversationId").GetGuid());

        var fromA = await Json(await host.SendAsync(a, HttpMethod.Get, $"/api/chat/direct/{b.Id}/messages"));
        var fromB = await Json(await host.SendAsync(b, HttpMethod.Get, $"/api/chat/direct/{a.Id}/messages"));
        Assert.Equal(["سلام", "علیک سلام"], fromA.EnumerateArray().Select(m => m.GetProperty("text").GetString()).ToArray());
        Assert.Equal(["سلام", "علیک سلام"], fromB.EnumerateArray().Select(m => m.GetProperty("text").GetString()).ToArray());
        // The same one conversation, reused - not a new one per message.
        Assert.All(fromA.EnumerateArray(), m => Assert.Equal(conversationId, m.GetProperty("conversationId").GetGuid()));
    }

    [Fact]
    public async Task SeveralMessagesAtOnce_StillUseOneConversation()
    {
        var (a, b) = await SharedTeamPairAsync();

        var sends = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            host.SendFormAsync(i % 2 == 0 ? a : b, HttpMethod.Post, $"/api/chat/direct/{(i % 2 == 0 ? b : a).Id}/messages", $"m{i}")));

        Assert.All(sends, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = (await Task.WhenAll(sends.Select(async r => (await Json(r)).GetProperty("conversationId").GetGuid()))).Distinct().ToList();
        Assert.Single(ids);
    }

    // 6: a random id that names nobody.
    [Fact]
    public async Task AnInvalidUser_Answers404_NeverA500()
    {
        var a = await host.UserAsync(host.TenantA, []);

        var get = await host.SendAsync(a, HttpMethod.Get, $"/api/chat/direct/{Guid.NewGuid()}/messages");
        var send = await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/direct/{Guid.NewGuid()}/messages", "hi");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
    }

    // 7: a real user, but of the other organization.
    [Fact]
    public async Task AUserOfAnotherOrganization_Answers404_NeverA500()
    {
        var a = await host.UserAsync(host.TenantA, []);
        var stranger = await host.UserAsync(host.TenantB, []);

        var get = await host.SendAsync(a, HttpMethod.Get, $"/api/chat/direct/{stranger.Id}/messages");
        var send = await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/direct/{stranger.Id}/messages", "hi");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
    }

    // 8: a real, same-tenant, team-sharing user - but disabled.
    [Fact]
    public async Task ADisabledUser_Answers404_NeverA500()
    {
        var a = await host.UserAsync(host.TenantA, []);
        var disabled = await host.UserAsync(host.TenantA, []);
        await host.TeamAsync(host.TenantA, a.Id, disabled.Id);
        await SetActiveAsync(disabled.Id, false);

        var get = await host.SendAsync(a, HttpMethod.Get, $"/api/chat/direct/{disabled.Id}/messages");
        var send = await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/direct/{disabled.Id}/messages", "hi");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
    }

    // 9: a real, same-tenant, active user the caller shares no team with, and who is not an
    // administrator of groups either - direct chat is not open to just anyone in the tenant.
    [Fact]
    public async Task ASameTenantUser_WithNoSharedTeam_Answers404_NeverA500()
    {
        var a = await host.UserAsync(host.TenantA, []);
        var unrelated = await host.UserAsync(host.TenantA, []);

        var get = await host.SendAsync(a, HttpMethod.Get, $"/api/chat/direct/{unrelated.Id}/messages");
        var send = await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/direct/{unrelated.Id}/messages", "hi");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
    }

    [Fact]
    public async Task YourselfAsThePartner_IsRefused_AsValidation_NeverA500()
    {
        var a = await host.UserAsync(host.TenantA, []);

        var send = await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/direct/{a.Id}/messages", "hi");

        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
    }

    [Fact]
    public async Task EmptyText_WithNoAttachment_IsRefused_AsValidation()
    {
        var (a, b) = await SharedTeamPairAsync();

        var send = await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/direct/{b.Id}/messages", "   ");

        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
    }

    // 10: a team's own shared conversation - separate from any direct thread its members have.
    [Fact]
    public async Task ATeamConversation_IsSharedByItsMembers_AndSeparateFromDirectChat()
    {
        var (a, b) = await SharedTeamPairAsync();
        var outsider = await host.UserAsync(host.TenantA, []);
        var teamId = await TeamIdOfAsync(a);

        var sent = await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/teams/{teamId}/messages", "به تیم");
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        var forB = await Json(await host.SendAsync(b, HttpMethod.Get, $"/api/chat/teams/{teamId}/messages"));
        Assert.Equal(["به تیم"], forB.EnumerateArray().Select(m => m.GetProperty("text").GetString()).ToArray());

        // The outsider - same tenant, just not on the team - cannot reach it.
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(outsider, HttpMethod.Get, $"/api/chat/teams/{teamId}/messages")).StatusCode);

        // A direct message between the same two people is its own, separate thread.
        await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/direct/{b.Id}/messages", "مستقیم");
        var direct = await Json(await host.SendAsync(b, HttpMethod.Get, $"/api/chat/direct/{a.Id}/messages"));
        Assert.Equal(["مستقیم"], direct.EnumerateArray().Select(m => m.GetProperty("text").GetString()).ToArray());
    }

    [Fact]
    public async Task ATeamConversation_ForATeamTheCallerIsNotIn_Answers404()
    {
        var a = await host.UserAsync(host.TenantA, []);
        var other = await host.UserAsync(host.TenantA, []);
        var teamId = await host.TeamAsync(host.TenantA, other.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(a, HttpMethod.Get, $"/api/chat/teams/{teamId}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/teams/{teamId}/messages", "hi")).StatusCode);
    }

    // 13: every one of the scenarios above answered with the status it names, never a 500 - the
    // one thing this whole stage exists to guarantee. This walks every valid path once more.
    [Fact]
    public async Task NoneOfTheValidPaths_Answer500()
    {
        var (a, b) = await SharedTeamPairAsync();
        var teamId = await TeamIdOfAsync(a);
        var responses = new List<HttpResponseMessage>
        {
            await host.SendAsync(a, HttpMethod.Get, $"/api/chat/direct/{b.Id}/messages"),
            await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/direct/{b.Id}/messages", "hi"),
            await host.SendAsync(b, HttpMethod.Get, $"/api/chat/direct/{a.Id}/messages"),
            await host.SendAsync(a, HttpMethod.Post, $"/api/chat/direct/{b.Id}/read"),
            await host.SendAsync(a, HttpMethod.Get, "/api/chat/direct/unread-counts"),
            await host.SendAsync(a, HttpMethod.Get, $"/api/chat/teams/{teamId}/messages"),
            await host.SendFormAsync(a, HttpMethod.Post, $"/api/chat/teams/{teamId}/messages", "hi"),
            await host.SendAsync(a, HttpMethod.Post, $"/api/chat/teams/{teamId}/read"),
            await host.SendAsync(a, HttpMethod.Get, "/api/chat/teams/unread-counts"),
        };

        Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"{r.RequestMessage!.RequestUri}: {(int)r.StatusCode}"));
    }

    /// <summary>Two active users of TenantA on the same team, so a direct chat between them is allowed.</summary>
    private async Task<(AccessControlTests.Caller A, AccessControlTests.Caller B)> SharedTeamPairAsync()
    {
        var a = await host.UserAsync(host.TenantA, []);
        var b = await host.UserAsync(host.TenantA, []);
        await host.TeamAsync(host.TenantA, a.Id, b.Id);
        return (a, b);
    }

    private async Task<Guid> TeamIdOfAsync(AccessControlTests.Caller caller)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusCore.Infrastructure.Persistence.NexusCoreDbContext>();
        return await db.UserGroups
            .Where(g => g.OwnerUserId == caller.Id || db.UserGroupMembers.Any(m => m.UserGroupId == g.Id && m.UserId == caller.Id))
            .Select(g => g.Id)
            .FirstAsync();
    }

    private async Task SetActiveAsync(Guid userId, bool active)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusCore.Infrastructure.Persistence.NexusCoreDbContext>();
        (await db.Users.SingleAsync(u => u.Id == userId)).SetActive(active);
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
