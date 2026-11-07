using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Actions;
using Nexus.Actions.Application;
using Nexus.Actions.Endpoints;
using Nexus.Actions.Infrastructure;
using Nexus.Integrations.ProjectCalendar;
using Nexus.Integrations.ProjectDeletion;
using Nexus.Integrations.ProjectGovernance;
using Nexus.Integrations.ProjectGovernance.Endpoints;
using Nexus.Integrations.ProjectVisibility;
using Nexus.Integrations.ProjectWorkflow;
using Nexus.Integrations.ProjectWorkflow.Endpoints;
using Nexus.Integrations.StrategyAlignment;
using Nexus.Integrations.StrategyAlignment.Endpoints;
using Nexus.Integrations.StrategyAlignment.Infrastructure;
using Nexus.Knowledge;
using Nexus.Knowledge.Endpoints;
using Nexus.Knowledge.Infrastructure;
using Nexus.Portfolio;
using Nexus.Portfolio.Endpoints;
using Nexus.ProjectManagement.Agile;
using Nexus.ProjectManagement.Agile.Endpoints;
using Nexus.ProjectManagement.Agile.Infrastructure;
using Nexus.ProjectManagement.Contracts;
using Nexus.ProjectManagement.Contracts.Endpoints;
using Nexus.ProjectManagement.Contracts.Infrastructure;
using Nexus.ProjectManagement.Core;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Core.Endpoints;
using Nexus.ProjectManagement.Core.Infrastructure;
using Nexus.ProjectManagement.Delegation;
using Nexus.ProjectManagement.Delegation.Endpoints;
using Nexus.ProjectManagement.Delegation.Infrastructure;
using Nexus.ProjectManagement.Deliverables;
using Nexus.ProjectManagement.Deliverables.Endpoints;
using Nexus.ProjectManagement.Deliverables.Infrastructure;
using Nexus.ProjectManagement.Documents;
using Nexus.ProjectManagement.Documents.Endpoints;
using Nexus.ProjectManagement.Documents.Infrastructure;
using Nexus.ProjectManagement.History;
using Nexus.ProjectManagement.History.Endpoints;
using Nexus.ProjectManagement.History.Infrastructure;
using Nexus.ProjectManagement.Kpi;
using Nexus.ProjectManagement.Kpi.Endpoints;
using Nexus.ProjectManagement.Kpi.Infrastructure;
using Nexus.ProjectManagement.Progress;
using Nexus.ProjectManagement.Progress.Endpoints;
using Nexus.ProjectManagement.Progress.Infrastructure;
using Nexus.ProjectManagement.RiskManagement;
using Nexus.ProjectManagement.RiskManagement.Endpoints;
using Nexus.ProjectManagement.RiskManagement.Infrastructure;
using Nexus.ProjectManagement.StakeholderManagement;
using Nexus.ProjectManagement.StakeholderManagement.Endpoints;
using Nexus.ProjectManagement.StakeholderManagement.Infrastructure;
using Nexus.ProjectManagement.Team;
using Nexus.ProjectManagement.Team.Endpoints;
using Nexus.ProjectManagement.Team.Infrastructure;
using Nexus.ProjectManagement.Waterfall;
using Nexus.ProjectManagement.Waterfall.Endpoints;
using Nexus.ProjectManagement.Waterfall.Infrastructure;
using Nexus.Reporting;
using Nexus.Reporting.Analytics;
using Nexus.Reporting.Endpoints;
using Nexus.StrategyManagement;
using Nexus.StrategyManagement.Endpoints;
using Nexus.StrategyManagement.Infrastructure;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.Bundles;

/// <summary>
/// Per-product tuning of the project-management modules. Every delegate is optional; a module without one keeps
/// its own defaults, so the bundle itself imposes no product rules.
/// </summary>
public sealed class NexusProjectManagementOptions
{
    public Action<ActionsOptions>? Actions { get; set; }
    public Action<ProjectOptions>? Project { get; set; }
    public Action<WaterfallOptions>? Waterfall { get; set; }
    public Action<AnalyticsOptions>? Analytics { get; set; }
}

/// <summary>
/// Every project-management module in three calls: <c>AddNexusProjectManagement</c>, <c>MapNexusProjectManagement</c>
/// and <c>InitializeNexusProjectManagementAsync</c>. Needs <c>AddNexusPlatform</c> (users, organization, calendar, workflow).
/// </summary>
public static class NexusProjectManagementBundle
{
    public static IServiceCollection AddNexusProjectManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<NexusProjectManagementOptions>? configure = null)
    {
        var options = new NexusProjectManagementOptions();
        configure?.Invoke(options);

        // Application tier
        services.AddActionManagement(options.Actions);
        services.AddKnowledgeManagement();
        services.AddStrategyManagement();

        services.AddProjectManagementCore(options.Project);
        services.AddWaterfallPlanning(options.Waterfall);
        services.AddAgilePlanning();
        services.AddProjectTeam();
        services.AddProjectDeliverables();
        services.AddProjectKpi();
        services.AddRiskManagement();
        services.AddStakeholderManagement();
        services.AddProgressManagement();
        services.AddProjectDocuments();
        services.AddContractManagement();
        services.AddProjectHistory();
        services.AddItemDelegation();

        services.AddProjectWorkflowIntegration();
        services.AddProjectGovernance();
        services.AddProjectVisibilityIntegration();
        services.AddProjectDeletionIntegration();
        services.AddProjectCalendarIntegration();
        services.AddProjectStrategyAlignment();

        services.AddPortfolio();
        services.AddProjectReporting();
        services.AddReportingAnalytics(options.Analytics);

        // Infrastructure tier
        services.AddActionManagementInfrastructure(configuration);
        services.AddKnowledgeManagementInfrastructure(configuration);
        services.AddStrategyManagementInfrastructure(configuration);

        services.AddProjectManagementCoreInfrastructure(configuration);
        services.AddWaterfallPlanningInfrastructure(configuration);
        services.AddAgilePlanningInfrastructure(configuration);
        services.AddProjectTeamInfrastructure(configuration);
        services.AddProjectDeliverablesInfrastructure(configuration);
        services.AddProjectKpiInfrastructure(configuration);
        services.AddRiskManagementInfrastructure(configuration);
        services.AddStakeholderManagementInfrastructure(configuration);
        services.AddProgressManagementInfrastructure(configuration);
        services.AddProjectDocumentsInfrastructure(configuration);
        services.AddContractManagementInfrastructure(configuration);
        services.AddProjectHistoryInfrastructure(configuration);
        services.AddItemDelegationInfrastructure(configuration);
        services.AddProjectStrategyAlignmentInfrastructure(configuration);

        return services;
    }

    public static WebApplication MapNexusProjectManagement(this WebApplication app)
    {
        app.MapActionEndpoints();
        app.MapKnowledgeDocumentEndpoints();
        app.MapStrategyEndpoints();

        app.MapProjectEndpoints();
        app.MapWaterfallEndpoints();
        app.MapAgileTaskEndpoints();
        app.MapTeamEndpoints();
        app.MapDeliverableEndpoints();
        app.MapKpiEndpoints();
        app.MapRiskEndpoints();
        app.MapStakeholderEndpoints();
        app.MapProgressEndpoints();
        app.MapProjectDocumentEndpoints();
        app.MapContractEndpoints();
        app.MapProjectHistoryEndpoints();
        app.MapItemDelegationEndpoints();
        app.MapProjectGovernanceEndpoints();

        app.MapProjectWorkflowEndpoints();
        app.MapProjectStrategyAlignmentEndpoints();

        app.MapPortfolioEndpoints();
        app.MapDashboardEndpoints();
        app.MapReportingAnalyticsEndpoints();
        return app;
    }

    /// <summary>Creates every project-management module's schema when <c>Database:SeedOnStartup</c> is on. Run it after <c>InitializeNexusPlatformAsync</c>.</summary>
    public static async Task InitializeNexusProjectManagementAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        if (!app.Configuration.GetValue("Database:SeedOnStartup", true))
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<ActionsDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<KnowledgeDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<StrategyManagementDbContext>(), cancellationToken);

        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<ProjectManagementCoreDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<WaterfallDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<AgileDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<TeamDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<DeliverablesDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<KpiDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<RiskManagementDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<StakeholderManagementDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<ProgressDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<ProjectDocumentsDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<ContractsDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<ProjectHistoryDbContext>(), cancellationToken);
        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<ItemDelegationDbContext>(), cancellationToken);

        await ModuleSchemaInitializer.EnsureCreatedAsync(sp.GetRequiredService<StrategyAlignmentDbContext>(), cancellationToken);
    }
}
