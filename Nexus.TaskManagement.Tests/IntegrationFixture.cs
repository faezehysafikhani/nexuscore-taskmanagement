using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;
using NexusCore.Domain.Identity;
using NexusCore.Application.Files;
using NexusCore.Infrastructure.Files;
using NexusCore.Infrastructure.Persistence;
using NexusCore.Infrastructure.Persistence.Repositories;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.TaskManagement.Tests;

/// <summary>
/// The signed-in user, swappable per test so ownership and tenant isolation can be exercised.
/// </summary>
public sealed class TestUserContext : ICurrentUserContext
{
    public Guid? UserId { get; set; }
    public Guid? TenantId { get; set; }
    public string? Email => "tester@example.com";
    public string? IpAddress => "127.0.0.1";
}

/// <summary>
/// A real SQL Server database, created once per run and dropped afterwards.
///
/// These tests deliberately do not use the in-memory provider: the rules being checked here -
/// CHECK constraints, unique filtered indexes, cascade behaviour, foreign keys into the
/// identity schema - are enforced by SQL Server and simply do not exist in memory. A green
/// in-memory run would prove nothing about them.
///
/// The database name is unique per run, so it can never collide with a developer's own.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string MasterConnectionString =
        "Server=.;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    public string DatabaseName { get; } = $"TaskMgmt_IT_{Guid.NewGuid():N}";

    public string ConnectionString =>
        $"Server=.;Database={DatabaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public bool Available { get; private set; }

    /// <summary>Real file storage on a throwaway folder, so uploads and downloads really round-trip.</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"TaskMgmt_IT_files_{Guid.NewGuid():N}");

    public Guid TenantId { get; } = Guid.NewGuid();
    public Guid OwnerUserId { get; } = Guid.NewGuid();
    public Guid OtherUserId { get; } = Guid.NewGuid();
    public Guid UserGroupId { get; } = Guid.NewGuid();

    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        try
        {
            await using (var master = new SqlConnection(MasterConnectionString))
            {
                await master.OpenAsync();
                await using var create = master.CreateCommand();
                create.CommandText = $"CREATE DATABASE [{DatabaseName}]";
                await create.ExecuteNonQueryAsync();
            }

            Available = true;
        }
        catch (Exception)
        {
            // No SQL Server here. Every test skips rather than reporting a false pass.
            Available = false;
            return;
        }

        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:RootPath"] = StorageRoot })
            .Build());
        services.AddScoped<IFileStorage, LocalDiskFileStorage>();
        services.AddScoped<IPlatformRepository, PlatformRepository>();
        services.AddScoped<IUploadPolicyReader, UploadPolicyReader>();
        services.AddSingleton<TestUserContext>();
        services.AddSingleton<ICurrentUserContext>(sp => sp.GetRequiredService<TestUserContext>());
        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();
        services.AddScoped<NexusCore.Application.Common.IDomainEventDispatcher, NoOpDomainEventDispatcher>();

        services.AddDbContext<NexusCoreDbContext>((sp, options) =>
            options.UseSqlServer(ConnectionString)
                .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));

        services.AddDbContext<TaskManagementDbContext>((sp, options) =>
            options.UseSqlServer(ConnectionString)
                .AddInterceptors(
                    sp.GetRequiredService<AuditingInterceptor>(),
                    sp.GetRequiredService<DomainEventDispatchInterceptor>()));

        services.AddScoped<ITaskManagementUnitOfWork>(sp => sp.GetRequiredService<TaskManagementDbContext>());
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IRepetitiveTaskRepository, RepetitiveTaskRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ITaskFileRepository, TaskFileRepository>();
        services.AddScoped<INoteRepository, NoteRepository>();
        services.AddScoped<ITaskCommentRepository, TaskCommentRepository>();
        services.AddScoped<ITaskActivityService, TaskActivityService>();
        services.AddSingleton<IRecurrenceCalculator, RecurrenceCalculator>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IRepetitiveTaskService, RepetitiveTaskService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<ITaskFileService, TaskFileService>();
        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<ITaskCommentService, TaskCommentService>();
        // These tests exercise the services' own rules, not who may see which task (that is
        // covered end to end in NexusCore.Tests' access tests), so every task is reachable here.
        services.AddScoped<ITaskAccessScope, UnrestrictedTaskAccessScope>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();

        // Identity first: TaskManagement's foreign keys point into it, so its tables must
        // exist before this module's are created. Same order Program.cs uses.
        var coreDb = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
        await ModuleSchemaInitializer.EnsureCreatedAsync(coreDb, CancellationToken.None);

        var taskDb = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
        await ModuleSchemaInitializer.EnsureCreatedAsync(taskDb, CancellationToken.None);

        coreDb.Tenants.Add(new Tenant(TenantId, "Test tenant", "test"));
        coreDb.Users.Add(new User(OwnerUserId, TenantId, "owner@example.com", "Owner", "hash"));
        coreDb.Users.Add(new User(OtherUserId, TenantId, "other@example.com", "Other", "hash"));
        coreDb.UserGroups.Add(new UserGroup(UserGroupId, TenantId, "Engineering"));
        await coreDb.SaveChangesAsync();
    }

    /// <summary>Runs a body in its own scope, as a request would.</summary>
    public async Task<T> ScopedAsync<T>(Func<IServiceProvider, Task<T>> body, Guid? actingAs = null)
    {
        using var scope = _provider!.CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<TestUserContext>();
        currentUser.TenantId = TenantId;
        currentUser.UserId = actingAs ?? OwnerUserId;
        return await body(scope.ServiceProvider);
    }

    public Task ScopedAsync(Func<IServiceProvider, Task> body, Guid? actingAs = null) =>
        ScopedAsync<object?>(async sp => { await body(sp); return null; }, actingAs);

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        try
        {
            if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true);
        }
        catch
        {
            // A leftover temp folder must not fail the run.
        }

        if (!Available) return;

        try
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(MasterConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText =
                $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]";
            await drop.ExecuteNonQueryAsync();
        }
        catch
        {
            // Leaving a scratch database behind is untidy but must not fail the run.
        }
    }
}

internal sealed class UnrestrictedTaskAccessScope : ITaskAccessScope
{
    public bool IsRestricted => false;
    public Guid? UserId => null;
    public IReadOnlyList<Guid> GroupIds => [];
    public Task EnsureLoadedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public bool CanManage(TaskItem task) => true;
}

/// <summary>
/// Domain events are dispatched by the interceptor after SaveChanges. These tests care about
/// the data, not the fan-out, so the dispatcher does nothing here.
/// </summary>
internal sealed class NoOpDomainEventDispatcher : NexusCore.Application.Common.IDomainEventDispatcher
{
    public Task DispatchAsync(
        IReadOnlyCollection<NexusCore.SharedKernel.Domain.IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

[CollectionDefinition("sqlserver")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
