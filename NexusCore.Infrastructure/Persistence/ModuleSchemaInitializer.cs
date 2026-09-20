using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace NexusCore.Infrastructure.Persistence;

/// <summary>
/// DbContext.Database.EnsureCreatedAsync() only creates tables the first time the physical
/// database itself doesn't exist yet: internally it checks "does this database have any tables
/// at all", not "does *this* context's own tables exist". Once any one module's DbContext has
/// created the shared DefaultConnection database, every other module's plain EnsureCreatedAsync()
/// call sees an existing, non-empty database and silently does nothing - its own tables never
/// get created. That matters here because every module owns its own DbContext, but a host that
/// composes several of them (see Rozet.Api) points them all at the same DefaultConnection
/// database, isolated by schema rather than by physical database (see each module's own
/// ToTable(name, schema) configuration). With no EF Core Migrations tooling available in this
/// environment to give each module a real, independent migration history, this instead talks to
/// the relational database creator directly: create the physical database if it is missing, then
/// unconditionally try to create *this* context's own tables, treating "already exists" (SQL
/// Server error 2714) as success so it stays safe to call again on every restart. A production
/// deployment with dotnet ef tooling available should replace this with real per-module
/// Migrations, which this cannot provide (no incremental schema changes, only initial creation).
/// </summary>
public static class ModuleSchemaInitializer
{
    public static async Task EnsureCreatedAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var creator = (IRelationalDatabaseCreator)dbContext.GetService<IDatabaseCreator>();

        if (!await creator.ExistsAsync(cancellationToken))
        {
            await creator.CreateAsync(cancellationToken);
        }

        try
        {
            await creator.CreateTablesAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 2714)
        {
        }
    }
}
