using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Nexus.Actions;
using Nexus.Actions.Application;
using Nexus.Actions.Infrastructure;
using Nexus.Knowledge;
using Nexus.Knowledge.Application;
using Nexus.Knowledge.Infrastructure;
using Nexus.ProjectManagement.Core;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Infrastructure;
using Nexus.ProjectManagement.RiskManagement;
using Nexus.ProjectManagement.RiskManagement.Application;
using Nexus.ProjectManagement.RiskManagement.Infrastructure;
using Nexus.ProjectManagement.Waterfall;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Infrastructure;
using Nexus.Workflow;
using Nexus.Workflow.Application;
using Nexus.Workflow.Infrastructure;
using NexusCore.Application;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Infrastructure;
using NexusCore.Infrastructure.Approvals;

namespace Nexus.CompositionTests;

/// <summary>
/// Actually builds an IServiceCollection with real combinations of AddXxx()/AddXxxInfrastructure()
/// calls and resolves services from it - this is the DI-graph-level equivalent of "start the app
/// with this module selection and see what breaks". AddDbContext + BuildServiceProvider does not
/// open a connection (EF Core connects lazily on first query), so these run without a real SQL
/// Server - only a syntactically valid connection string, never queried, is enough to prove the
/// composition wires up.
/// </summary>
public sealed class DiCompositionTests
{
    private static IConfiguration BuildConfiguration() => new FakeConnectionStringConfiguration();

    /// <summary>
    /// Stands in for ConfigurationBuilder().AddInMemoryCollection(...) without a dependency on
    /// the Microsoft.Extensions.Configuration.Memory package - only GetConnectionString("DefaultConnection")
    /// needs to resolve, which routes through GetSection("ConnectionStrings") then an indexer read;
    /// everything else here is unused by AddDbContext/UseSqlServer and never called.
    /// </summary>
    private sealed class FakeConnectionStringConfiguration : IConfiguration
    {
        private const string ConnectionString = "Server=.;Database=CompositionTests;Trusted_Connection=True;TrustServerCertificate=True";

        public string? this[string key]
        {
            get => key is "DefaultConnection" or "ConnectionStrings:DefaultConnection" ? ConnectionString : null;
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string key) => new ConfigurationSection(this, key);

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => NullChangeToken.Instance;

        private sealed class ConfigurationSection(IConfiguration root, string path) : IConfigurationSection
        {
            public string? this[string key]
            {
                get => root[key];
                set => throw new NotSupportedException();
            }

            public string Key => path;
            public string Path => path;
            public string? Value { get => root[path]; set => throw new NotSupportedException(); }

            public IConfigurationSection GetSection(string key) => root.GetSection(key);
            public IEnumerable<IConfigurationSection> GetChildren() => [];
            public IChangeToken GetReloadToken() => NullChangeToken.Instance;
        }

        private sealed class NullChangeToken : IChangeToken
        {
            public static readonly NullChangeToken Instance = new();
            public bool HasChanged => false;
            public bool ActiveChangeCallbacks => false;
            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => NullDisposable.Instance;

            private sealed class NullDisposable : IDisposable
            {
                public static readonly NullDisposable Instance = new();
                public void Dispose() { }
            }
        }
    }

    [Fact]
    public void NexusCoreOnly_ComposesWithoutAnyModule()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IIdentityService>());
        Assert.IsType<NullApprovalRequester>(scope.ServiceProvider.GetRequiredService<IApprovalRequester>());
    }

    [Fact]
    public void ProjectManagementCore_ComposesAlone_WithoutAnyOptionalCapability()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(BuildConfiguration());
        services.AddProjectManagementCore();
        services.AddProjectManagementCoreInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProjectManagementUnitOfWork>());
        // No capability installed: default approval behavior, no Workflow override.
        Assert.IsType<NullApprovalRequester>(scope.ServiceProvider.GetRequiredService<IApprovalRequester>());
    }

    [Fact]
    public void Waterfall_ComposesWithProjectManagementCore()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(BuildConfiguration());
        services.AddProjectManagementCore();
        services.AddProjectManagementCoreInfrastructure(BuildConfiguration());
        services.AddWaterfallPlanning();
        services.AddWaterfallPlanningInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProjectManagementUnitOfWork>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWaterfallUnitOfWork>());
    }

    [Fact]
    public void Risk_WithoutWorkflow_FallsBackToNullApprovalRequester()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(BuildConfiguration());
        services.AddProjectManagementCore();
        services.AddProjectManagementCoreInfrastructure(BuildConfiguration());
        services.AddRiskManagement();
        services.AddRiskManagementInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRiskUnitOfWork>());
        Assert.IsType<NullApprovalRequester>(scope.ServiceProvider.GetRequiredService<IApprovalRequester>());
    }

    [Fact]
    public void Risk_WithWorkflow_OverridesToWorkflowApprovalRequester()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(BuildConfiguration());
        services.AddProjectManagementCore();
        services.AddProjectManagementCoreInfrastructure(BuildConfiguration());
        services.AddRiskManagement();
        services.AddRiskManagementInfrastructure(BuildConfiguration());
        services.AddWorkflowApplication();
        services.AddWorkflowInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Order-independent: Workflow's own DI replaces the default regardless of registration
        // order (see Nexus.Workflow/DependencyInjection.cs's use of services.Replace).
        Assert.IsType<WorkflowApprovalRequester>(scope.ServiceProvider.GetRequiredService<IApprovalRequester>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowUnitOfWork>());
    }

    [Fact]
    public void ActionManagement_ComposesStandalone_WithoutProjectManagement()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(BuildConfiguration());
        services.AddActionManagement();
        services.AddActionManagementInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IActionsUnitOfWork>());
        Assert.Null(scope.ServiceProvider.GetService<IProjectManagementUnitOfWork>());
    }

    [Fact]
    public void Knowledge_ComposesStandalone_WithoutProjectManagement()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(BuildConfiguration());
        services.AddKnowledgeManagement();
        services.AddKnowledgeManagementInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IKnowledgeUnitOfWork>());
        Assert.Null(scope.ServiceProvider.GetService<IProjectManagementUnitOfWork>());
    }

    [Fact]
    public void DisabledCapability_ItsServicesAreNotRegisteredAtAll()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(BuildConfiguration());
        services.AddProjectManagementCore();
        services.AddProjectManagementCoreInfrastructure(BuildConfiguration());
        // Risk Management and Waterfall Planning deliberately not installed.

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<IRiskUnitOfWork>());
        Assert.Null(scope.ServiceProvider.GetService<IWaterfallUnitOfWork>());
    }

    [Fact]
    public void MultipleCapabilities_EachGetsItsOwnUnitOfWork_NoDataLossAcrossModules()
    {
        // Regression guard for the bug this platform hit repeatedly during development: sharing
        // one non-keyed IUnitOfWork registration across modules made every module but the last
        // registered one silently lose its changes. Each module's marker interface must resolve
        // independently.
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(BuildConfiguration());
        services.AddProjectManagementCore();
        services.AddProjectManagementCoreInfrastructure(BuildConfiguration());
        services.AddWaterfallPlanning();
        services.AddWaterfallPlanningInfrastructure(BuildConfiguration());
        services.AddRiskManagement();
        services.AddRiskManagementInfrastructure(BuildConfiguration());
        services.AddActionManagement();
        services.AddActionManagementInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var projectManagementUnitOfWork = scope.ServiceProvider.GetRequiredService<IProjectManagementUnitOfWork>();
        var waterfallUnitOfWork = scope.ServiceProvider.GetRequiredService<IWaterfallUnitOfWork>();
        var riskUnitOfWork = scope.ServiceProvider.GetRequiredService<IRiskUnitOfWork>();
        var actionsUnitOfWork = scope.ServiceProvider.GetRequiredService<IActionsUnitOfWork>();

        Assert.NotSame((object)projectManagementUnitOfWork, (object)waterfallUnitOfWork);
        Assert.NotSame((object)waterfallUnitOfWork, (object)riskUnitOfWork);
        Assert.NotSame((object)riskUnitOfWork, (object)actionsUnitOfWork);
    }
}
