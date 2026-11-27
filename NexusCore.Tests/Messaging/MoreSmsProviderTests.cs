using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NexusCore.Application.Messaging;
using NexusCore.Infrastructure.Messaging;

namespace NexusCore.Tests.Messaging;

/// <summary>The extra SMS gateways (OPEN-02): the request each one makes, how it reads the answer, and that a failure never shows the key.</summary>
public sealed class MoreSmsProviderTests
{
    private sealed class Fake(HttpStatusCode status, string body) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public bool Throw { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (Throw) throw new HttpRequestException("boom https://secret.example/KEY123");
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    }

    private static SmsProviderSettings Settings(string? key = "KEY123", string? line = "3000", string? user = null, string? pass = null) =>
        new("", key, line, user, pass);

    [Fact]
    public async Task Melipayamak_PostsJson_AndReadsRetStatus()
    {
        var fake = new Fake(HttpStatusCode.OK, """{"Value":"98765","RetStatus":1,"StrRetStatus":"Ok"}""");
        var provider = new MelipayamakSmsProvider(fake, NullLogger<MelipayamakSmsProvider>.Instance);

        var sent = await provider.SendAsync(Settings(key: null, user: "u", pass: "p"), "09121234567", "hello", default);
        Assert.True(sent.IsSuccess);
        Assert.Equal("98765", sent.Value);
        Assert.Equal("https://rest.payamak-panel.com/api/SendSMS/SendSMS", fake.Request!.RequestUri!.ToString());
        Assert.Contains("\"username\":\"u\"", fake.Body);
        Assert.Contains("\"to\":\"09121234567\"", fake.Body);

        var refused = await new MelipayamakSmsProvider(new Fake(HttpStatusCode.OK, """{"Value":"","RetStatus":3,"StrRetStatus":"InvalidAccount"}"""), NullLogger<MelipayamakSmsProvider>.Instance)
            .SendAsync(Settings(key: null, user: "u", pass: "p"), "0912", "x", default);
        Assert.True(refused.IsFailure);
        Assert.Contains("InvalidAccount", refused.Error.Message);

        Assert.True((await provider.SendAsync(Settings(key: null), "0912", "x", default)).IsFailure);
    }

    [Fact]
    public async Task Ghasedak_SendsTheKeyInAHeader_AndReadsResultCode()
    {
        var fake = new Fake(HttpStatusCode.OK, """{"result":{"code":200,"message":"success"},"items":[1234]}""");
        var provider = new GhasedakSmsProvider(fake, NullLogger<GhasedakSmsProvider>.Instance);

        var sent = await provider.SendAsync(Settings(), "09121234567", "hello", default);
        Assert.True(sent.IsSuccess);
        Assert.Equal("1234", sent.Value);
        Assert.Equal("KEY123", fake.Request!.Headers.GetValues("apikey").Single());
        Assert.DoesNotContain("KEY123", fake.Request.RequestUri!.ToString());
        Assert.Contains("receptor=09121234567", fake.Body);
        Assert.Contains("linenumber=3000", fake.Body);

        var refused = await new GhasedakSmsProvider(new Fake(HttpStatusCode.OK, """{"result":{"code":412,"message":"bad line"}}"""), NullLogger<GhasedakSmsProvider>.Instance)
            .SendAsync(Settings(), "0912", "x", default);
        Assert.Contains("bad line", refused.Error.Message);
        Assert.True((await provider.SendAsync(Settings(key: ""), "0912", "x", default)).IsFailure);
    }

    [Fact]
    public async Task SmsIr_SendsJsonWithAKeyHeader_AndReadsStatus()
    {
        var fake = new Fake(HttpStatusCode.OK, """{"status":1,"message":"موفق","data":{"packId":"p-9","messageIds":[1]}}""");
        var provider = new SmsIrSmsProvider(fake, NullLogger<SmsIrSmsProvider>.Instance);

        var sent = await provider.SendAsync(Settings(line: "30007732"), "09121234567", "hello", default);
        Assert.True(sent.IsSuccess);
        Assert.Equal("p-9", sent.Value);
        Assert.Equal("KEY123", fake.Request!.Headers.GetValues("X-API-KEY").Single());
        Assert.Contains("\"mobiles\":[\"09121234567\"]", fake.Body);
        Assert.Contains("\"lineNumber\":30007732", fake.Body);

        var refused = await new SmsIrSmsProvider(new Fake(HttpStatusCode.OK, """{"status":104,"message":"credit"}"""), NullLogger<SmsIrSmsProvider>.Instance)
            .SendAsync(Settings(), "0912", "x", default);
        Assert.Contains("credit", refused.Error.Message);
        Assert.True((await provider.SendAsync(Settings(line: null), "0912", "x", default)).IsFailure);
    }

    [Theory]
    [InlineData("melipayamak")]
    [InlineData("ghasedak")]
    [InlineData("smsir")]
    public async Task ANetworkFailure_NeverLeaksTheKeyOrTheUrl_AndAPageThatIsNotJson_IsRefused(string which)
    {
        var broken = new Fake(HttpStatusCode.OK, "{}") { Throw = true };
        var html = new Fake(HttpStatusCode.BadGateway, "<html>proxy</html>");
        ISmsProvider Make(Fake f) => which switch
        {
            "melipayamak" => new MelipayamakSmsProvider(f, NullLogger<MelipayamakSmsProvider>.Instance),
            "ghasedak" => new GhasedakSmsProvider(f, NullLogger<GhasedakSmsProvider>.Instance),
            _ => new SmsIrSmsProvider(f, NullLogger<SmsIrSmsProvider>.Instance)
        };

        var s = Settings(user: "u", pass: "p");
        var failed = await Make(broken).SendAsync(s, "0912", "x", default);
        Assert.True(failed.IsFailure);
        Assert.DoesNotContain("KEY123", failed.Error.Message);
        Assert.DoesNotContain("secret.example", failed.Error.Message);

        var notJson = await Make(html).SendAsync(s, "0912", "x", default);
        Assert.True(notJson.IsFailure);
        Assert.Contains("502", notJson.Error.Message);
    }

    [Fact]
    public void TheNewProviders_HaveDistinctKeys()
    {
        var keys = new ISmsProvider[]
        {
            new KavenegarSmsProvider(new Fake(HttpStatusCode.OK, "{}"), NullLogger<KavenegarSmsProvider>.Instance),
            new MelipayamakSmsProvider(new Fake(HttpStatusCode.OK, "{}"), NullLogger<MelipayamakSmsProvider>.Instance),
            new GhasedakSmsProvider(new Fake(HttpStatusCode.OK, "{}"), NullLogger<GhasedakSmsProvider>.Instance),
            new SmsIrSmsProvider(new Fake(HttpStatusCode.OK, "{}"), NullLogger<SmsIrSmsProvider>.Instance),
        }.Select(p => p.Key).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }
}
