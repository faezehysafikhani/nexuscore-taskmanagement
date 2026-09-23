using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;
using NexusCore.Domain.Identity;

namespace Nexus.CompositionTests;

/// <summary>
/// Loading one task with everything under it, on a real relational database. A recurring task
/// with its occurrences, tags, files and collaborators used to come back as one query whose
/// collections multiplied each other's rows (thousands of rows for one task, enough to time
/// out); it is now read one collection per query - and still completely.
/// </summary>
public sealed class TaskDetailLoadTests : IDisposable
{
    private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"task-detail-{Guid.NewGuid():N}.db");
    private readonly RowCounter _counter = new();
    private readonly DbContextOptions<TaskManagementDbContext> _options;
    private readonly Guid _tenant = Guid.NewGuid();

    public TaskDetailLoadTests()
    {
        _options = new DbContextOptionsBuilder<TaskManagementDbContext>()
            .UseSqlite($"Data Source={_databaseFile};Pooling=False")
            .ReplaceService<IModelCustomizer, SqliteCustomizer>()
            .AddInterceptors(_counter)
            .Options;
        using var db = new TaskManagementDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_databaseFile);
        }
        catch (IOException)
        {
            // A leftover temp file is harmless.
        }
    }

    [Fact]
    public async Task ARecurringTaskWithEverythingUnderIt_LoadsCompletely_WithoutMultiplyingRows()
    {
        const int occurrences = 60;
        var taskId = await SeedAsync(occurrences, people: 3, tags: 3, files: 3);

        _counter.Reset();
        await using var db = new TaskManagementDbContext(_options);
        var task = await new TaskRepository(db).GetByIdAsync(_tenant, taskId, default);

        Assert.NotNull(task);
        Assert.Equal(occurrences, task.SubTasks.Count);
        Assert.All(task.SubTasks, s => Assert.Equal(2, s.Tags.Count(t => t.Tag is not null)));
        Assert.Equal(3, task.Tags.Count(t => t.Tag is not null));
        Assert.Equal(3, task.Files.Count(f => f.File is not null));
        Assert.Equal(3, task.Assignees.Count);

        // One query per collection, and about as many rows as there are records - a single query
        // returned 60 x 2 x 3 x 3 x 3 = 3,240 rows here.
        Assert.True(_counter.Commands > 1, $"expected split queries, got {_counter.Commands}");
        Assert.True(_counter.Rows < 300, $"read {_counter.Rows} rows for one task");
    }

    [Fact]
    public async Task AnotherOrganization_StillDoesNotGetTheTask()
    {
        var taskId = await SeedAsync(occurrences: 5, people: 1, tags: 1, files: 1);

        await using var db = new TaskManagementDbContext(_options);
        Assert.Null(await new TaskRepository(db).GetByIdAsync(Guid.NewGuid(), taskId, default));
    }

    private async Task<Guid> SeedAsync(int occurrences, int people, int tags, int files)
    {
        await using var db = new TaskManagementDbContext(_options);
        var owner = new User(Guid.NewGuid(), _tenant, null, "Owner", "not-used");
        var collaborators = Enumerable.Range(0, people).Select(i => new User(Guid.NewGuid(), _tenant, null, $"User {i}", "not-used")).ToList();
        db.Users.Add(owner);
        db.Users.AddRange(collaborators);

        var task = new TaskItem(Guid.NewGuid(), _tenant, "Daily report", new DateOnly(2026, 1, 1), TaskPriority.Medium, false, owner.Id);
        task.AssignUsers(collaborators.Select(c => c.Id).ToList());
        for (var i = 0; i < occurrences; i++)
        {
            task.AddSubTask(Guid.NewGuid(), $"Occurrence {i}", SubTaskImportance.Medium, i).MarkGeneratedOccurrence(true);
        }

        db.Tasks.Add(task);
        var tagRows = Enumerable.Range(0, tags).Select(i => new Tag(Guid.NewGuid(), _tenant, $"tag {i}")).ToList();
        db.Tags.AddRange(tagRows);
        await db.SaveChangesAsync();

        foreach (var tag in tagRows)
        {
            db.TaskTags.Add(TaskTag.ForTask(Guid.NewGuid(), tag.Id, task.Id));
        }

        foreach (var subTask in task.SubTasks)
        {
            foreach (var tag in tagRows.Take(2))
            {
                db.TaskTags.Add(TaskTag.ForSubTask(Guid.NewGuid(), tag.Id, subTask.Id));
            }
        }

        for (var i = 0; i < files; i++)
        {
            var asset = new TaskFileAsset(Guid.NewGuid(), _tenant, $"file{i}.pdf", $"stored{i}", "application/pdf", 1000, "path", owner.Id);
            db.Files.Add(asset);
            db.TaskFiles.Add(TaskFile.ForTask(Guid.NewGuid(), asset.Id, task.Id));
        }

        await db.SaveChangesAsync();
        return task.Id;
    }

    /// <summary>Counts the queries run and the rows they returned.</summary>
    private sealed class RowCounter : DbCommandInterceptor
    {
        public int Commands { get; private set; }
        public int Rows { get; private set; }

        public void Reset() => Commands = Rows = 0;

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Commands++;
            return ValueTask.FromResult<DbDataReader>(new CountingReader(result, () => Rows++));
        }
    }

    private sealed class CountingReader(DbDataReader inner, Action onRow) : DbDataReader
    {
        public override bool Read() { var more = inner.Read(); if (more) onRow(); return more; }
        public override async Task<bool> ReadAsync(CancellationToken cancellationToken) { var more = await inner.ReadAsync(cancellationToken); if (more) onRow(); return more; }
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => inner.IsDBNullAsync(ordinal, cancellationToken);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override bool NextResult() => inner.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => inner.NextResultAsync(cancellationToken);
        public override System.Collections.IEnumerator GetEnumerator() => ((System.Collections.IEnumerable)inner).GetEnumerator();
        public override Task CloseAsync() => inner.CloseAsync();
        public override void Close() => inner.Close();
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); }
    }

    /// <summary>SQLite cannot compare DateTimeOffset; the shared identity tables are created here too.</summary>
    private sealed class SqliteCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.IsTableExcludedFromMigrations()))
            {
                entityType.SetIsTableExcludedFromMigrations(false);
            }

            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            }
        }
    }
}
