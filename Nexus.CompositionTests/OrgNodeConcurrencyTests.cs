using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Infrastructure;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Infrastructure.Persistence;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.CompositionTests;

/// <summary>Runs only against a real SQL Server: NEXUS_TEST_SQLSERVER = a connection string to a server where it may create a database.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NEXUS_TEST_SQLSERVER")))
        {
            Skip = "NEXUS_TEST_SQLSERVER is not set (needs a real SQL Server: rowversion and sp_getapplock).";
        }
    }
}

/// <summary>
/// Two people changing the structure at the same moment, on SQL Server (whose rowversion and
/// application lock the node API relies on): an edit over an older version is refused, and two
/// moves that would together make a loop never both succeed.
/// </summary>
public sealed class OrgNodeConcurrencyTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class Db : IAsyncDisposable
    {
        private readonly string _server;

        public Db()
        {
            _server = Environment.GetEnvironmentVariable("NEXUS_TEST_SQLSERVER")!;
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_server) { InitialCatalog = "NexusOrgNodes_" + Guid.NewGuid().ToString("N") };
            Connection = builder.ConnectionString;
        }

        public string Connection { get; }

        public async Task CreateAsync()
        {
            await using var organization = Organization();
            await ModuleSchemaInitializer.EnsureCreatedAsync(organization, default);
            await using var structure = Structure();
            await ModuleSchemaInitializer.EnsureCreatedAsync(structure, default);
        }

        public OrganizationDbContext Organization() => new(new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(Connection).Options);

        public OrgStructureDbContext Structure() => new(new DbContextOptionsBuilder<OrgStructureDbContext>().UseSqlServer(Connection).Options);

        /// <summary>The node API of one request (its own contexts, the SQL Server lock).</summary>
        public (IOrgNodeService Service, IAsyncDisposable Scope) Request()
        {
            var organization = Organization();
            var structure = Structure();
            var service = new OrgNodeService(
                new OrgStructureRepository(structure), structure, new OrganizationUnitRepository(organization), organization, new NoUsers(),
                new SqlOrgStructureWriteLock(structure));
            return (service, new Both(organization, structure));
        }

        public async ValueTask DisposeAsync()
        {
            await using var structure = Structure();
            await structure.Database.EnsureDeletedAsync();
        }
    }

    private sealed class Both(DbContext a, DbContext b) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await a.DisposeAsync();
            await b.DisposeAsync();
        }
    }

    private static async Task<OrgNodeDto> Ok(Task<NexusCore.SharedKernel.Results.Result<OrgNodeDto>> call)
    {
        var result = await call;
        Assert.True(result.IsSuccess, result.IsSuccess ? null : $"{result.Error.Code}: {result.Error.Message}");
        return result.Value!;
    }

    /// <summary>A deputy type, a unit type under it and under itself, and two nodes A and B.</summary>
    private static async Task<(Guid A, Guid B)> SeedAsync(Db db)
    {
        var (setup, scope) = db.Request();
        await using (scope)
        {
            var type = (await setup.CreateNodeTypeAsync(Tenant, new CreateNodeTypeRequest("واحد", "unit"), default)).Value!.Id;
            await setup.SetRulesAsync(Tenant, new SetNodeTypeRulesRequest([new NodeTypeRuleDto(null, type), new NodeTypeRuleDto(type, type)]), default);
            var a = await Ok(setup.CreateNodeAsync(Tenant, new CreateNodeRequest("الف", NodeTypeId: type), default));
            var b = await Ok(setup.CreateNodeAsync(Tenant, new CreateNodeRequest("ب", NodeTypeId: type), default));
            return (a.Id, b.Id);
        }
    }

    [SqlServerFact]
    public async Task AnEditOverAnOlderVersion_IsRefused()
    {
        await using var db = new Db();
        await db.CreateAsync();
        var (a, _) = await SeedAsync(db);

        // Two people open the node; each saves with the version they read.
        var (first, firstScope) = db.Request();
        var (second, secondScope) = db.Request();
        await using (firstScope)
        await using (secondScope)
        {
            var read1 = await Ok(first.GetNodeAsync(Tenant, a, default));
            var read2 = await Ok(second.GetNodeAsync(Tenant, a, default));
            Assert.Equal(read1.RowVersion, read2.RowVersion);
            Assert.NotEqual(string.Empty, read1.RowVersion);

            var saved = await Ok(first.UpdateNodeAsync(Tenant, a, new UpdateNodeRequest("الف (اول)", null, read1.RowVersion), default));
            Assert.NotEqual(read1.RowVersion, saved.RowVersion);
            var refused = await second.UpdateNodeAsync(Tenant, a, new UpdateNodeRequest("الف (دوم)", null, read2.RowVersion), default);
            Assert.Equal("conflict", refused.Error.Code);
        }

        var (check, checkScope) = db.Request();
        await using (checkScope)
        {
            Assert.Equal("الف (اول)", (await Ok(check.GetNodeAsync(Tenant, a, default))).Name);
        }
    }

    [SqlServerFact]
    public async Task DetailsOverAnOlderVersion_AreRefused()
    {
        await using var db = new Db();
        await db.CreateAsync();
        var (a, _) = await SeedAsync(db);

        var (first, firstScope) = db.Request();
        var (second, secondScope) = db.Request();
        await using (firstScope)
        await using (secondScope)
        {
            var read1 = await Ok(first.GetNodeAsync(Tenant, a, default));
            var read2 = await Ok(second.GetNodeAsync(Tenant, a, default));

            var saved = await Ok(first.SetDetailsAsync(Tenant, a, new SetNodeDetailsRequest(Phone: "021", RowVersion: read1.RowVersion), default));
            Assert.NotEqual(read1.RowVersion, saved.RowVersion);
            var refused = await second.SetDetailsAsync(Tenant, a, new SetNodeDetailsRequest(Phone: "031", RowVersion: read2.RowVersion), default);
            Assert.Equal("conflict", refused.Error.Code);
        }
    }

    [SqlServerFact]
    public async Task TwoMovesAtTheSameMoment_NeverMakeALoop()
    {
        await using var db = new Db();
        await db.CreateAsync();
        var (a, b) = await SeedAsync(db);

        // A under B and B under A, at the same moment, without versions: each is fine alone.
        for (var round = 0; round < 5; round++)
        {
            var (one, oneScope) = db.Request();
            var (two, twoScope) = db.Request();
            await using (oneScope)
            await using (twoScope)
            {
                var results = await Task.WhenAll(
                    one.MoveNodeAsync(Tenant, a, new MoveNodeRequest(b), default),
                    two.MoveNodeAsync(Tenant, b, new MoveNodeRequest(a), default));
                Assert.Single(results, result => result.IsSuccess);
            }

            var (check, checkScope) = db.Request();
            await using (checkScope)
            {
                var nodeA = await Ok(check.GetNodeAsync(Tenant, a, default));
                var nodeB = await Ok(check.GetNodeAsync(Tenant, b, default));
                Assert.False(nodeA.ParentNodeId == b && nodeB.ParentNodeId == a, "A and B ended up under each other.");
                // Back to the top for the next round.
                await Ok(check.MoveNodeAsync(Tenant, a, new MoveNodeRequest(null), default));
                await Ok(check.MoveNodeAsync(Tenant, b, new MoveNodeRequest(null), default));
            }
        }
    }

    private sealed class NoUsers : IUserDirectory
    {
        public Task<IReadOnlyList<UserContact>> GetUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UserContact>>([]);
        public Task<IReadOnlyList<UserContact>> GetActiveUsersInTenantAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UserContact>>([]);
        public Task<IReadOnlyList<Guid>> GetGroupMemberIdsAsync(Guid groupId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<IReadOnlyList<Guid>> GetGroupIdsOfUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
