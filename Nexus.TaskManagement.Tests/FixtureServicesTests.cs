using Microsoft.Extensions.DependencyInjection;

namespace Nexus.TaskManagement.Tests;

/// <summary>
/// No database needed: the services the integration tests resolve can all be built. This is what catches a service the
/// task module started to need (a seam of a product) that the test host forgot to register, instead of 40 tests failing
/// one by one on a machine that has SQL Server.
/// </summary>
public sealed class FixtureServicesTests
{
    [Fact]
    public void EveryServiceOfTheIntegrationHost_CanBeBuilt()
    {
        var services = SqlServerFixture.BuildServices("Server=.;Database=unused;Trusted_Connection=True;TrustServerCertificate=True", Path.GetTempPath());
        var failure = Record.Exception(() => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        Assert.True(failure is null, failure?.ToString());
    }
}
