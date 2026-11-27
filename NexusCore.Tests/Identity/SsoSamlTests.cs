using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Web;
using System.Xml;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Sso;
using NexusCore.Application.Platform.Interfaces;
using Xunit;

namespace NexusCore.Tests.Identity;

/// <summary>The SAML answer is trusted only when it is signed by the provider, meant for us, fresh and used once.</summary>
public sealed class SsoSamlTests
{
    private const string Acs = "https://api.test/api/identity/auth/sso/idp/acs";
    private const string Sp = "https://tm.test/sp";
    private const string Idp = "https://idp.test/entity";

    public class Recorder<T> : DispatchProxy where T : class
    {
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add($"{method!.Name}:{args?.FirstOrDefault()}");
            var type = method.ReturnType;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = type.GetGenericArguments()[0];
                var value = inner.IsValueType ? Activator.CreateInstance(inner) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [value]);
            }

            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }

    private static (SsoService Service, Recorder<IIdentityRepository> Repo, X509Certificate2 Cert) Build()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=idp", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        cert = new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);

        var repo = DispatchProxy.Create<IIdentityRepository, Recorder<IIdentityRepository>>();
        var platform = DispatchProxy.Create<IPlatformService, Recorder<IPlatformService>>();
        var options = Options.Create(new SsoOptions
        {
            Providers =
            [
                new SsoProviderOptions
                {
                    Key = "idp", Type = SsoProviderType.Saml, IdpSsoUrl = "https://idp.test/sso", IdpEntityId = Idp, SpEntityId = Sp, AcsUrl = Acs,
                    IdpCertificate = Convert.ToBase64String(cert.Export(X509ContentType.Cert)),
                },
            ],
        });
        var service = new SsoService(options, repo, null!, platform, new EphemeralDataProtectionProvider(), null!, NullLogger<SsoService>.Instance);
        return (service, (Recorder<IIdentityRepository>)(object)repo, cert);
    }

    private static async Task<(string RequestId, string RelayState)> StartAsync(SsoService service)
    {
        var started = await service.StartAsync("idp", null, CancellationToken.None);
        var query = HttpUtility.ParseQueryString(new Uri(started.Value!.RedirectUrl).Query);
        using var inflated = new DeflateStream(new MemoryStream(Convert.FromBase64String(query["SAMLRequest"]!)), CompressionMode.Decompress);
        var xml = new StreamReader(inflated).ReadToEnd();
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return (doc.DocumentElement!.GetAttribute("ID"), query["RelayState"]!);
    }

    private static string Answer(X509Certificate2 cert, string requestId, string nameId = "0012345678", string audience = Sp, string assertionId = "_a1", bool sign = true, DateTimeOffset? notOnOrAfter = null)
    {
        var after = (notOnOrAfter ?? DateTimeOffset.UtcNow.AddMinutes(5)).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var xml = $"""
            <samlp:Response xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol" xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion" ID="_r1" Version="2.0" IssueInstant="{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}" Destination="{Acs}" InResponseTo="{requestId}">
              <saml:Issuer>{Idp}</saml:Issuer>
              <samlp:Status><samlp:StatusCode Value="urn:oasis:names:tc:SAML:2.0:status:Success"/></samlp:Status>
              <saml:Assertion ID="{assertionId}" Version="2.0" IssueInstant="{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}">
                <saml:Issuer>{Idp}</saml:Issuer>
                <saml:Subject><saml:NameID>{nameId}</saml:NameID>
                  <saml:SubjectConfirmation Method="urn:oasis:names:tc:SAML:2.0:cm:bearer"><saml:SubjectConfirmationData Recipient="{Acs}" InResponseTo="{requestId}" NotOnOrAfter="{after}"/></saml:SubjectConfirmation>
                </saml:Subject>
                <saml:Conditions NotBefore="{DateTime.UtcNow.AddMinutes(-1):yyyy-MM-ddTHH:mm:ssZ}" NotOnOrAfter="{after}"><saml:AudienceRestriction><saml:Audience>{audience}</saml:Audience></saml:AudienceRestriction></saml:Conditions>
              </saml:Assertion>
            </samlp:Response>
            """;
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        if (sign)
        {
            var assertion = (XmlElement)doc.GetElementsByTagName("Assertion", "urn:oasis:names:tc:SAML:2.0:assertion")[0]!;
            var signed = new SignedXml(doc) { SigningKey = cert.GetRSAPrivateKey() };
            var reference = new Reference("#" + assertionId);
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigExcC14NTransform());
            signed.AddReference(reference);
            signed.ComputeSignature();
            assertion.InsertAfter(doc.ImportNode(signed.GetXml(), true), assertion.FirstChild);
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(doc.OuterXml));
    }

    private static bool Reached(Recorder<IIdentityRepository> repo, string nameId) => repo.Calls.Contains($"FindUserByLoginAsync:{nameId}");

    [Fact]
    public async Task A_signed_answer_for_this_request_reaches_the_user_lookup()
    {
        var (service, repo, cert) = Build();
        var (id, relay) = await StartAsync(service);
        await service.CompleteSamlAsync("idp", Answer(cert, id), relay, CancellationToken.None);
        Assert.True(Reached(repo, "0012345678"));
    }

    [Fact]
    public async Task An_unsigned_answer_is_refused()
    {
        var (service, repo, cert) = Build();
        var (id, relay) = await StartAsync(service);
        var result = await service.CompleteSamlAsync("idp", Answer(cert, id, sign: false), relay, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.False(Reached(repo, "0012345678"));
    }

    [Fact]
    public async Task A_changed_name_after_signing_is_refused()
    {
        var (service, repo, cert) = Build();
        var (id, relay) = await StartAsync(service);
        var xml = Encoding.UTF8.GetString(Convert.FromBase64String(Answer(cert, id))).Replace("0012345678", "0099999999");
        await service.CompleteSamlAsync("idp", Convert.ToBase64String(Encoding.UTF8.GetBytes(xml)), relay, CancellationToken.None);
        Assert.False(Reached(repo, "0099999999"));
    }

    [Fact]
    public async Task Another_signing_key_is_refused()
    {
        var (service, repo, _) = Build();
        var (_, _, other) = Build();
        var (id, relay) = await StartAsync(service);
        await service.CompleteSamlAsync("idp", Answer(other, id), relay, CancellationToken.None);
        Assert.False(Reached(repo, "0012345678"));
    }

    [Fact]
    public async Task An_answer_for_another_audience_or_request_is_refused()
    {
        var (service, repo, cert) = Build();
        var (id, relay) = await StartAsync(service);
        await service.CompleteSamlAsync("idp", Answer(cert, id, audience: "https://other/sp"), relay, CancellationToken.None);
        await service.CompleteSamlAsync("idp", Answer(cert, "_someone_elses_request", assertionId: "_a2"), relay, CancellationToken.None);
        Assert.False(Reached(repo, "0012345678"));
    }

    [Fact]
    public async Task An_expired_answer_is_refused()
    {
        var (service, repo, cert) = Build();
        var (id, relay) = await StartAsync(service);
        await service.CompleteSamlAsync("idp", Answer(cert, id, notOnOrAfter: DateTimeOffset.UtcNow.AddMinutes(-10)), relay, CancellationToken.None);
        Assert.False(Reached(repo, "0012345678"));
    }

    [Fact]
    public async Task An_answer_works_once()
    {
        var (service, repo, cert) = Build();
        var (id, relay) = await StartAsync(service);
        var answer = Answer(cert, id, assertionId: "_once");
        await service.CompleteSamlAsync("idp", answer, relay, CancellationToken.None);
        repo.Calls.Clear();
        await service.CompleteSamlAsync("idp", answer, relay, CancellationToken.None);
        Assert.False(Reached(repo, "0012345678"));
    }

    [Fact]
    public async Task A_tampered_relay_state_is_refused()
    {
        var (service, repo, cert) = Build();
        var (id, _) = await StartAsync(service);
        await service.CompleteSamlAsync("idp", Answer(cert, id), "not-a-state", CancellationToken.None);
        Assert.False(Reached(repo, "0012345678"));
    }
}
