using System.Net;
using Microsoft.Extensions.Options;
using NexusCore.Application.Security;

namespace NexusCore.Tests.Identity;

/// <summary>Where an administrator-edited setting may point the server (SMS gateway, LDAP).</summary>
public sealed class OutboundAddressTests
{
    private static readonly string[] KavenegarOnly = ["https://api.kavenegar.com/v1"];

    private static OutboundAddressPolicy Policy(OutboundAddressOptions? options = null, params (string Host, string Address)[] dns) =>
        new(Options.Create(options ?? new OutboundAddressOptions()),
            (host, _) => Task.FromResult(dns.Where(entry => entry.Host == host).Select(entry => IPAddress.Parse(entry.Address)).ToArray()));

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]   // the cloud metadata service
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("2606:4700::1111", true)]
    public void IsPublic_SeparatesTheInternetFromTheInside(string address, bool expected) =>
        Assert.Equal(expected, OutboundAddressPolicy.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public async Task TheProvidersOwnAddress_IsAccepted_WhenItResolvesToPublicAddresses()
    {
        var policy = Policy(null, ("api.kavenegar.com", "185.1.2.3"));

        Assert.True((await policy.ValidateGatewayUrlAsync("https://api.kavenegar.com/v1", KavenegarOnly, default)).IsSuccess);
    }

    [Theory]
    [InlineData("https://attacker.example/v1")]            // not the provider's, not listed
    [InlineData("http://api.kavenegar.com/v1")]            // not https
    [InlineData("https://user:pass@api.kavenegar.com/v1")] // credentials in the address
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("ftp://api.kavenegar.com/v1")]
    public async Task OtherAddresses_AreRefused(string url)
    {
        var policy = Policy(null, ("api.kavenegar.com", "185.1.2.3"));

        Assert.True((await policy.ValidateGatewayUrlAsync(url, KavenegarOnly, default)).IsFailure);
    }

    [Fact]
    public async Task AProviderNameThatPointsInside_IsRefused()
    {
        // The provider's name now answers with a private address (DNS tampering or rebinding).
        var policy = Policy(null, ("api.kavenegar.com", "10.0.0.8"));

        Assert.True((await policy.ValidateGatewayUrlAsync("https://api.kavenegar.com/v1", KavenegarOnly, default)).IsFailure);
    }

    [Fact]
    public async Task AHostTheOperatorListed_MayBeInsideTheNetwork()
    {
        var options = new OutboundAddressOptions { AllowedHosts = ["sms.bank.local", "*.gw.bank.local"] };
        var policy = Policy(options, ("sms.bank.local", "10.20.30.40"));

        Assert.True((await policy.ValidateGatewayUrlAsync("https://sms.bank.local/api", KavenegarOnly, default)).IsSuccess);
        Assert.True((await policy.ValidateGatewayUrlAsync("https://one.gw.bank.local/api", KavenegarOnly, default)).IsSuccess);
        Assert.True((await policy.ValidateGatewayUrlAsync("http://sms.bank.local/api", KavenegarOnly, default)).IsFailure);
        Assert.True((await Policy(new OutboundAddressOptions { AllowedHosts = ["sms.bank.local"], AllowHttp = true })
            .ValidateGatewayUrlAsync("http://sms.bank.local/api", KavenegarOnly, default)).IsSuccess);
        Assert.True((await policy.ValidateGatewayUrlAsync("https://evil-gw.bank.local.attacker.example/api", KavenegarOnly, default)).IsFailure);
    }

    [Theory]
    [InlineData("127.0.0.1", 389, false)]
    [InlineData("169.254.169.254", 389, false)]
    [InlineData("10.0.0.5", 389, true)]       // a directory server is normally inside the network
    [InlineData("10.0.0.5", 636, true)]
    [InlineData("10.0.0.5", 22, false)]       // the test button must not scan other services
    [InlineData("10.0.0.5", 6379, false)]
    public async Task LdapServer_IsCheckedByAddressAndPort(string host, int port, bool allowed)
    {
        var policy = Policy();

        Assert.Equal(allowed, (await policy.ValidateLdapServerAsync(host, port, default)).IsSuccess);
    }

    [Fact]
    public async Task LdapServer_CanBeLimitedToListedHosts()
    {
        var policy = Policy(new OutboundAddressOptions { LdapAllowedHosts = ["dc1.bank.local"] }, ("dc1.bank.local", "10.0.0.5"), ("dc2.bank.local", "10.0.0.6"));

        Assert.True((await policy.ValidateLdapServerAsync("dc1.bank.local", 389, default)).IsSuccess);
        Assert.True((await policy.ValidateLdapServerAsync("dc2.bank.local", 389, default)).IsFailure);
    }

    [Fact]
    public void EveryCaptchaCharacter_CanBeDrawn()
    {
        foreach (var code in new[] { "23456789", "ACEFHKLM", "NPRTUXY2" })
        {
            var png = NexusCore.Infrastructure.Security.CaptchaImage.Render(code);
            Assert.True(png.Length > 100);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        }
    }
}
