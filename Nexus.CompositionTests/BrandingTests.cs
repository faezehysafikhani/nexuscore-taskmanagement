using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.CompositionTests;

/// <summary>
/// The system's name, logo and tab icon: everyone reads them (the sign-in page too), only
/// settings.update changes them, and only real PNG / JPEG / WebP / ICO images are kept.
/// </summary>
public sealed class BrandingTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Ico = [0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x10];

    private static MultipartFormDataContent Form(string? name = null, (string Field, string File, byte[] Bytes)? image = null, params string[] flags)
    {
        var form = new MultipartFormDataContent();
        if (name is not null)
        {
            form.Add(new StringContent(name), "systemName");
        }

        if (image is { } i)
        {
            form.Add(new ByteArrayContent(i.Bytes) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } }, i.Field, i.File);
        }

        foreach (var flag in flags)
        {
            form.Add(new StringContent("true"), flag);
        }

        return form;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Branding_IsReadByEveryone_AndChangedOnlyWithSettingsUpdate()
    {
        var anonymous = new AccessControlTests.Caller(Guid.Empty, "not-a-token");
        var member = await host.UserAsync(host.TenantA, [IdentityPermissions.UsersView]);

        // Nothing set yet: the product's own defaults.
        var initial = await Json(await host.SendAsync(anonymous, HttpMethod.Get, "/api/platform/branding"));
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("logoUrl").ValueKind);

        // Not theirs to change.
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(member, HttpMethod.Put, "/api/platform/branding", Form("نام دیگر"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(anonymous, HttpMethod.Put, "/api/platform/branding", Form("نام دیگر"))).StatusCode);

        // The administrator sets the name and a logo (an SVG or a renamed text file is refused).
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/branding",
            Form(image: ("logo", "logo.svg", Encoding.UTF8.GetBytes("<svg onload=\"alert(1)\"/>"))))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/branding",
            Form(image: ("logo", "logo.png", Encoding.UTF8.GetBytes("not an image"))))).StatusCode);
        var saved = await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/branding", Form("سامانه وظایف بانک", ("logo", "logo.png", Png)));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/branding", Form(image: ("favicon", "tab.ico", Ico)))).StatusCode);

        // Everyone, signed in or not, gets it.
        var branding = await Json(await host.SendAsync(anonymous, HttpMethod.Get, "/api/platform/branding"));
        Assert.Equal("سامانه وظایف بانک", branding.GetProperty("systemName").GetString());
        var logoUrl = branding.GetProperty("logoUrl").GetString()!;
        var faviconUrl = branding.GetProperty("faviconUrl").GetString()!;
        var logo = await host.SendAsync(anonymous, HttpMethod.Get, logoUrl);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await logo.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/x-icon", (await host.SendAsync(member, HttpMethod.Get, faviconUrl)).Content.Headers.ContentType!.MediaType);

        // The generic settings API is no way around the checks above.
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/settings",
            new { tenantId = (Guid?)null, key = "Branding.Logo", value = "../appsettings.json|image/png", scope = "System" })).StatusCode);

        // Removing the logo keeps the name and the tab icon.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/branding", Form(flags: "removeLogo"))).StatusCode);
        var after = await Json(await host.SendAsync(anonymous, HttpMethod.Get, "/api/platform/branding"));
        Assert.Equal(JsonValueKind.Null, after.GetProperty("logoUrl").ValueKind);
        Assert.Equal("سامانه وظایف بانک", after.GetProperty("systemName").GetString());
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("faviconUrl").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(anonymous, HttpMethod.Get, "/api/platform/branding/logo")).StatusCode);

        // An empty name goes back to the product's own.
        await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/branding", Form(""));
        Assert.Equal(JsonValueKind.Null, (await Json(await host.SendAsync(anonymous, HttpMethod.Get, "/api/platform/branding"))).GetProperty("systemName").ValueKind);
    }
}
