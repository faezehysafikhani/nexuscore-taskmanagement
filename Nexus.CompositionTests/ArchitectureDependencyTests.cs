using System.Text.RegularExpressions;

namespace Nexus.CompositionTests;

/// <summary>
/// Enforces the platform's dependency rules directly against the .csproj files on disk, so a
/// future PR that adds a stray ProjectReference (e.g. ProjectManagement.Core reaching into
/// Waterfall, or Workflow reaching into ProjectManagement) fails a test instead of only being
/// caught in review. No project reference is needed to any of the modules checked here - this
/// reads their .csproj files as text, the same way it would check a module nobody has written
/// C# bindings for yet.
/// </summary>
public sealed class ArchitectureDependencyTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    [Fact]
    public void NexusCoreFoundation_DoesNotReferenceAnyNexusModule()
    {
        foreach (var project in new[]
                 {
                     "NexusCore.Domain/NexusCore.Domain.csproj",
                     "NexusCore.SharedKernel/NexusCore.SharedKernel.csproj",
                     "NexusCore.Application/NexusCore.Application.csproj",
                     "NexusCore.Infrastructure/NexusCore.Infrastructure.csproj"
                 })
        {
            AssertNoReferenceStartingWith(project, "Nexus.");
        }
    }

    [Fact]
    public void ProjectManagementCore_DoesNotReferenceAnyOptionalCapability()
    {
        var forbidden = new[]
        {
            "Nexus.ProjectManagement.Waterfall", "Nexus.ProjectManagement.Agile",
            "Nexus.ProjectManagement.Team", "Nexus.ProjectManagement.Deliverables",
            "Nexus.ProjectManagement.Kpi", "Nexus.ProjectManagement.Risk",
            "Nexus.ProjectManagement.Stakeholder", "Nexus.ProjectManagement.Progress",
            "Nexus.ProjectManagement.Documents", "Nexus.Workflow"
        };

        foreach (var project in new[]
                 {
                     "Nexus.ProjectManagement.Core/Nexus.ProjectManagement.Core.csproj",
                     "Nexus.ProjectManagement.Core.Infrastructure/Nexus.ProjectManagement.Core.Infrastructure.csproj"
                 })
        {
            var references = GetProjectReferences(project);
            Assert.Empty(references.Intersect(forbidden));
        }
    }

    [Fact]
    public void Workflow_IsGeneric_DoesNotReferenceProjectManagementOrIntegrations()
    {
        foreach (var project in new[] { "Nexus.Workflow/Nexus.Workflow.csproj", "Nexus.Workflow.Infrastructure/Nexus.Workflow.Infrastructure.csproj" })
        {
            AssertNoReferenceStartingWith(project, "Nexus.ProjectManagement.");
            AssertNoReferenceStartingWith(project, "Nexus.Integrations.");
        }
    }

    [Fact]
    public void ActionManagement_IsIndependent_DoesNotReferenceProjectManagement()
    {
        foreach (var project in new[] { "Nexus.Actions/Nexus.Actions.csproj", "Nexus.Actions.Infrastructure/Nexus.Actions.Infrastructure.csproj" })
        {
            AssertNoReferenceStartingWith(project, "Nexus.ProjectManagement.");
        }
    }

    [Fact]
    public void KnowledgeAndStrategy_AreStandalone_DoNotReferenceProjectManagementOrIntegrations()
    {
        foreach (var project in new[]
                 {
                     "Nexus.Knowledge/Nexus.Knowledge.csproj", "Nexus.Knowledge.Infrastructure/Nexus.Knowledge.Infrastructure.csproj",
                     "Nexus.Strategy/Nexus.Strategy.csproj", "Nexus.Strategy.Infrastructure/Nexus.Strategy.Infrastructure.csproj"
                 })
        {
            AssertNoReferenceStartingWith(project, "Nexus.ProjectManagement.");
            AssertNoReferenceStartingWith(project, "Nexus.Integrations.");
        }
    }

    [Fact]
    public void AgileAndWaterfall_DoNotReferenceEachOther()
    {
        var waterfallRefs = GetProjectReferences("Nexus.ProjectManagement.Waterfall/Nexus.ProjectManagement.Waterfall.csproj");
        var agileRefs = GetProjectReferences("Nexus.ProjectManagement.Agile/Nexus.ProjectManagement.Agile.csproj");

        Assert.DoesNotContain("Nexus.ProjectManagement.Agile", waterfallRefs);
        Assert.DoesNotContain("Nexus.ProjectManagement.Waterfall", agileRefs);
    }

    [Fact]
    public void Progress_DoesNotReferenceWaterfall()
    {
        var references = GetProjectReferences("Nexus.ProjectManagement.Progress/Nexus.ProjectManagement.Progress.csproj");
        Assert.DoesNotContain("Nexus.ProjectManagement.Waterfall", references);
    }

    [Fact]
    public void PortfolioAndReporting_OwnNoDatabase()
    {
        Assert.False(Directory.Exists(Path.Combine(SolutionRoot, "Nexus.Portfolio.Infrastructure")));
        Assert.False(Directory.Exists(Path.Combine(SolutionRoot, "Nexus.Reporting.Infrastructure")));
    }

    [Fact]
    public void Integrations_AreNotReferencedByTheModulesTheyIntegrate()
    {
        AssertNoReferenceStartingWith("Nexus.ProjectManagement.Core/Nexus.ProjectManagement.Core.csproj", "Nexus.Integrations.");
        AssertNoReferenceStartingWith("Nexus.Workflow/Nexus.Workflow.csproj", "Nexus.Integrations.");
        AssertNoReferenceStartingWith("Nexus.Strategy/Nexus.Strategy.csproj", "Nexus.Integrations.");
    }

    [Fact]
    public void ProjectStrategyAlignment_ReferencesBothSidesItIntegrates()
    {
        var references = GetProjectReferences("Nexus.Integrations.ProjectStrategyAlignment/Nexus.Integrations.ProjectStrategyAlignment.csproj");
        Assert.Contains("Nexus.ProjectManagement.Core", references);
        Assert.Contains("Nexus.Strategy", references);
    }

    private static void AssertNoReferenceStartingWith(string relativeProjectPath, string forbiddenPrefix)
    {
        var references = GetProjectReferences(relativeProjectPath);
        Assert.DoesNotContain(references, r => r.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> GetProjectReferences(string relativeProjectPath)
    {
        var fullPath = Path.Combine(SolutionRoot, relativeProjectPath);
        var content = File.ReadAllText(fullPath);

        return Regex.Matches(content, @"<ProjectReference Include=""([^""]+)""")
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToList();
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexusCore.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate NexusCore.sln above the test output directory.");
    }
}
