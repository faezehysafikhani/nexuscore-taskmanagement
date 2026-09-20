using System.Text.RegularExpressions;

namespace Nexus.CompositionTests;

/// <summary>
/// Pins down where the line between "shared" and "business" actually falls, now that
/// TaskManagement holds real foreign keys into the identity schema.
///
/// NexusCore (Identity, Authentication, Authorization, Role, Permission, UserGroup, Tenant) is
/// shared infrastructure: a business module is *expected* to depend on it and to reference
/// User/UserGroup with real keys, instead of growing its own duplicate user table.
///
/// Every other module is a business module. TaskManagement must stay independent of those, and
/// they must stay independent of it - that is the part of "modular monolith" these tests still
/// enforce. Nothing in the existing ArchitectureDependencyTests is weakened by this file; this
/// only adds the new module's own rules.
/// </summary>
public sealed class TaskManagementIsolationTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    private const string TaskManagement = "Nexus.TaskManagement/Nexus.TaskManagement.csproj";

    private const string TaskManagementInfrastructure =
        "Nexus.TaskManagement.Infrastructure/Nexus.TaskManagement.Infrastructure.csproj";

    /// <summary>The business modules TaskManagement must never reach into.</summary>
    private static readonly string[] BusinessModules =
    [
        "Chat.Domain", "Chat.Application", "Chat.Infrastructure", "Chat.Api",
        "Ticketing.Domain", "Ticketing.Application", "Ticketing.Infrastructure", "Ticketing.Api",
        "Notifications.Domain", "Notifications.Application", "Notifications.Infrastructure", "Notifications.Api",
        "Events.Domain", "Events.Application", "Events.Infrastructure", "Events.Api"
    ];

    [Fact]
    public void TaskManagement_DoesNotReferenceAnyOtherBusinessModule()
    {
        foreach (var project in new[] { TaskManagement, TaskManagementInfrastructure })
        {
            var references = GetProjectReferences(project);

            Assert.Empty(references.Intersect(BusinessModules));
            Assert.DoesNotContain(references, r => r.StartsWith("Nexus.ProjectManagement.", StringComparison.Ordinal));
            Assert.DoesNotContain(references, r => r.StartsWith("Nexus.Integrations.", StringComparison.Ordinal));
            Assert.DoesNotContain(references, r => r.StartsWith("Nexus.Workflow", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NoOtherModule_ReferencesTaskManagement()
    {
        foreach (var projectFile in Directory.EnumerateFiles(SolutionRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(projectFile);

            // The composition host is allowed to reference everything - that is its job.
            if (name is "Rozet.Api" or "Nexus.CompositionTests") continue;

            // The module itself, and its own tests.
            if (name.StartsWith("Nexus.TaskManagement", StringComparison.Ordinal)) continue;

            // Integration projects are the sanctioned bridge between two modules: they are
            // allowed to know both sides precisely so neither module has to know the other.
            // The same exemption the existing ProjectStrategyAlignment test relies on.
            if (name.StartsWith("Nexus.Integrations.", StringComparison.Ordinal)) continue;

            var references = GetProjectReferences(Path.GetRelativePath(SolutionRoot, projectFile));
            Assert.DoesNotContain(references, r => r.StartsWith("Nexus.TaskManagement", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The shared-infrastructure exception, stated positively: depending on NexusCore is not
    /// only allowed, it is required. If this ever fails, someone has copied User or UserGroup
    /// into the module instead of referencing the real one.
    /// </summary>
    [Fact]
    public void TaskManagement_DependsOnSharedNexusCoreFoundation()
    {
        Assert.Contains("NexusCore.Application", GetProjectReferences(TaskManagement));
        Assert.Contains("NexusCore.Infrastructure", GetProjectReferences(TaskManagementInfrastructure));
    }

    /// <summary>
    /// TaskManagement may *map* the shared identity tables so EF can emit real foreign keys,
    /// but it must never own them: every borrowed mapping is ExcludeFromMigrations, and the
    /// generated migration must not create or drop anything in the identity schema.
    /// </summary>
    [Fact]
    public void TaskManagement_BorrowsIdentityTables_ButNeverOwnsThem()
    {
        var sharedConfig = Path.Combine(
            SolutionRoot, "Nexus.TaskManagement.Infrastructure", "Configurations", "SharedIdentityConfigurations.cs");

        Assert.True(File.Exists(sharedConfig));

        var content = File.ReadAllText(sharedConfig);
        var mappings = Regex.Matches(content, @"ToTable\(""(?<table>[^""]+)"",\s*""identity""").ToList();

        Assert.NotEmpty(mappings);

        // Each borrowed mapping must opt out of migrations in its own statement. Counting
        // occurrences across the whole file would also count the ones in the doc comment.
        foreach (var mapping in mappings)
        {
            var terminator = content.IndexOf(';', mapping.Index);
            var statement = terminator < 0
                ? content[mapping.Index..]
                : content[mapping.Index..terminator];

            Assert.Contains("ExcludeFromMigrations", statement);
        }

        var migrationsDirectory = Path.Combine(SolutionRoot, "Nexus.TaskManagement.Infrastructure", "Migrations");
        foreach (var migration in Directory.EnumerateFiles(migrationsDirectory, "*.cs")
                     .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal)))
        {
            var body = File.ReadAllText(migration);
            foreach (var statement in Regex.Matches(body, @"(CreateTable|DropTable)\((?<args>[^;]*?)\);", RegexOptions.Singleline)
                         .Select(m => m.Groups["args"].Value))
            {
                Assert.DoesNotContain("\"identity\"", statement);
            }
        }
    }

    /// <summary>
    /// The notification bridge must stay a bridge: it may know both sides, but neither side
    /// may know it, or the coupling it exists to prevent comes back through the side door.
    /// </summary>
    [Fact]
    public void TaskNotificationsIntegration_ReferencesBothSidesAndIsReferencedByNeither()
    {
        const string integration = "Nexus.Integrations.TaskNotifications/Nexus.Integrations.TaskNotifications.csproj";
        if (!File.Exists(Path.Combine(SolutionRoot, integration)))
        {
            return;
        }

        var references = GetProjectReferences(integration);
        Assert.Contains("Nexus.TaskManagement", references);
        Assert.Contains("Notifications.Application", references);

        Assert.DoesNotContain(
            GetProjectReferences(TaskManagement),
            r => r.StartsWith("Nexus.Integrations.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            GetProjectReferences(TaskManagementInfrastructure),
            r => r.StartsWith("Nexus.Integrations.", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> GetProjectReferences(string relativeProjectPath)
    {
        var content = File.ReadAllText(Path.Combine(SolutionRoot, relativeProjectPath));

        return Regex.Matches(content, @"<ProjectReference Include=""([^""]+)""")
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList()!;
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
