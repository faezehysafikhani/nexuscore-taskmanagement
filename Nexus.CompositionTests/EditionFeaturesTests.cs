using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusCore.Application;
using NexusCore.Application.Editions;
using NexusCore.Application.Endpoints;

namespace Nexus.CompositionTests;

/// <summary>Commercial features: off unless the host turns them on, all at once or one by one.</summary>
public sealed class EditionFeaturesTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void WithNothingSet_EveryFeatureIsOff()
    {
        var config = Config();
        Assert.All(CommercialFeature.All, feature => Assert.False(EditionFeatures.IsEnabled(config, feature)));
    }

    [Fact]
    public void TheCommercialEdition_TurnsEverythingOn_AndOneFeatureCanStillBeSwitchedOff()
    {
        var config = Config(("Edition:Commercial", "true"), ("Edition:Features:Sso", "false"));
        Assert.False(EditionFeatures.IsEnabled(config, CommercialFeature.Sso));
        Assert.True(EditionFeatures.IsEnabled(config, CommercialFeature.Mfa));
    }

    [Fact]
    public void AnotherEdition_CanBeGivenOneFeature()
    {
        var config = Config(("Edition:Features:Mfa", "true"));
        Assert.True(EditionFeatures.IsEnabled(config, CommercialFeature.Mfa));
        Assert.False(EditionFeatures.IsEnabled(config, CommercialFeature.Sso));
    }

    private static IReadOnlyList<string> SecurityRoutes(params (string Key, string Value)[] values)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value));
        builder.Services.AddApplication();
        var app = builder.Build();
        app.MapIdentitySecurityEndpoints();
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText ?? "").ToList();
    }

    [Fact]
    public void AHostWithoutTheEdition_MapsNoneOfTheSecurityEndpoints()
    {
        Assert.Empty(SecurityRoutes());
    }

    [Fact]
    public void TheCommercialHost_MapsThem_ExceptAFeatureItSwitchedOff()
    {
        var routes = SecurityRoutes(("Edition:Commercial", "true"), ("Edition:Features:Sso", "false"));
        Assert.Contains(routes, route => route.Contains("/login/mfa"));
        Assert.Contains(routes, route => route.Contains("/service-accounts"));
        Assert.DoesNotContain(routes, route => route.Contains("/sso/"));
    }
}
