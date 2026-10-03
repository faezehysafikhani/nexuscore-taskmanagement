using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Nexus.ProjectManagement.Documents.Application;
using Nexus.ProjectManagement.Documents.Application.Dtos;
using Nexus.ProjectManagement.Documents.Domain;
using Nexus.ProjectManagement.Documents.Infrastructure;
using NexusCore.Application.Approvals;
using NexusCore.Application.Files;
using NexusCore.SharedKernel.Results;

namespace Nexus.CompositionTests;

public sealed class DocumentVersionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();

    private sealed class FakeRepository : IProjectDocumentRepository
    {
        public List<ProjectDocument> Documents { get; } = [];
        public List<ProjectDocumentVersion> Versions { get; } = [];

        public Task<ProjectDocument?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Documents.SingleOrDefault(d => d.Id == id));
        public Task<IReadOnlyList<ProjectDocument>> ListByProjectAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProjectDocument>>(Documents.Where(d => d.ProjectId == projectId).ToList());
        public Task AddAsync(ProjectDocument document, CancellationToken ct) { Documents.Add(document); return Task.CompletedTask; }
        public Task RemoveAsync(ProjectDocument document, CancellationToken ct) { Documents.Remove(document); return Task.CompletedTask; }
        public Task<IReadOnlyList<ProjectDocumentVersion>> ListVersionsAsync(Guid documentId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProjectDocumentVersion>>(Versions.Where(v => v.DocumentId == documentId).OrderBy(v => v.VersionNumber).ToList());
        public Task AddVersionAsync(ProjectDocumentVersion version, CancellationToken ct) { Versions.Add(version); return Task.CompletedTask; }
        public Task RemoveVersionsAsync(IReadOnlyCollection<ProjectDocumentVersion> versions, CancellationToken ct)
        {
            foreach (var version in versions) { Versions.Remove(version); }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public List<string> Deleted { get; } = [];
        private int _counter;

        public async Task<StoredFile> SaveAsync(string fileName, string contentType, Stream content, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            var key = $"key-{++_counter}";
            Files[key] = buffer.ToArray();
            return new StoredFile(key, fileName, contentType, buffer.Length);
        }

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string storageKey, CancellationToken ct)
        {
            Deleted.Add(storageKey);
            Files.Remove(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IDocumentsUnitOfWork
    {
        public bool FailNextSave { get; set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("database is down");
            }

            return Task.FromResult(1);
        }
    }

    private sealed class FakeApprovals(ApprovalRequestOutcome outcome = ApprovalRequestOutcome.NotConfigured) : IApprovalRequester
    {
        public Task<ApprovalRequestOutcome> RequestApprovalAsync(ApprovalSubject subject, CancellationToken ct) => Task.FromResult(outcome);
    }

    private sealed record Fixture(ProjectDocumentService Service, FakeRepository Repository, FakeStorage Storage, FakeUnitOfWork UnitOfWork);

    private static Fixture Build(ApprovalRequestOutcome outcome = ApprovalRequestOutcome.NotConfigured)
    {
        var repository = new FakeRepository();
        var storage = new FakeStorage();
        var unitOfWork = new FakeUnitOfWork();
        return new Fixture(new ProjectDocumentService(repository, storage, unitOfWork, new FakeApprovals(outcome)), repository, storage, unitOfWork);
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<Guid> UploadAsync(Fixture fixture, string text = "v1 content", string fileName = "plan.pdf")
    {
        var result = await fixture.Service.UploadAsync(
            new UploadProjectDocumentRequest(Tenant, Project, "Site plan", ProjectDocumentType.Report, fileName, "application/pdf"),
            Bytes(text), default);
        Assert.True(result.IsSuccess);
        return result.Value!.Id;
    }

    private static UploadProjectDocumentVersionRequest NextVersion(string fileName = "plan-v2.pdf", string? comment = "fixed the east wall") =>
        new(fileName, "application/pdf", comment);

    private static async Task<string> ReadAsync(Result<(Stream Content, string FileName, string ContentType)> result)
    {
        Assert.True(result.IsSuccess);
        using var reader = new StreamReader(result.Value.Content);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Upload_StartsTheHistoryAtVersionOne()
    {
        var fixture = Build();

        var id = await UploadAsync(fixture);

        var document = (await fixture.Service.GetAsync(id, default)).Value!;
        Assert.Equal(1, document.CurrentVersion);
        var version = Assert.Single(fixture.Repository.Versions);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(id, version.DocumentId);
        Assert.Equal("plan.pdf", version.FileName);
    }

    [Fact]
    public async Task UploadVersion_MakesTheNewFileCurrent_AndKeepsTheOldOneReachable()
    {
        var fixture = Build();
        var id = await UploadAsync(fixture, "first");

        var uploaded = await fixture.Service.UploadVersionAsync(id, NextVersion(), Bytes("second"), default);

        Assert.True(uploaded.IsSuccess);
        Assert.Equal(2, uploaded.Value!.VersionNumber);
        Assert.True(uploaded.Value.IsCurrent);
        Assert.Equal("fixed the east wall", uploaded.Value.Comment);

        // The document, and its existing download endpoint, now serve the new file...
        var document = (await fixture.Service.GetAsync(id, default)).Value!;
        Assert.Equal(2, document.CurrentVersion);
        Assert.Equal("plan-v2.pdf", document.FileName);
        Assert.Equal("second", await ReadAsync(await fixture.Service.DownloadAsync(id, default)));

        // ...and the original is still there, byte for byte, and was never deleted.
        Assert.Equal("first", await ReadAsync(await fixture.Service.DownloadVersionAsync(id, 1, default)));
        Assert.Equal("second", await ReadAsync(await fixture.Service.DownloadVersionAsync(id, 2, default)));
        Assert.Empty(fixture.Storage.Deleted);
        Assert.Equal(2, fixture.Storage.Files.Count);
    }

    [Fact]
    public async Task Versions_AreListedNewestFirst_WithOnlyTheLatestFlaggedCurrent()
    {
        var fixture = Build();
        var id = await UploadAsync(fixture);
        await fixture.Service.UploadVersionAsync(id, NextVersion("b.pdf", "b"), Bytes("b"), default);
        await fixture.Service.UploadVersionAsync(id, NextVersion("c.pdf", null), Bytes("c"), default);

        var versions = (await fixture.Service.ListVersionsAsync(id, default)).Value!;

        Assert.Equal([3, 2, 1], versions.Select(v => v.VersionNumber));
        Assert.Equal([true, false, false], versions.Select(v => v.IsCurrent));
        Assert.Equal(["c.pdf", "b.pdf", "plan.pdf"], versions.Select(v => v.FileName));
        Assert.Null(versions[0].Comment);
    }

    [Fact]
    public async Task ANewVersion_SendsAnApprovedDocumentBackToUnsubmitted()
    {
        var fixture = Build();
        var id = await UploadAsync(fixture);
        await fixture.Service.SubmitForApprovalAsync(id, default); // no Workflow: approved directly
        Assert.Equal(ApprovalStatus.Approved, (await fixture.Service.GetAsync(id, default)).Value!.ApprovalStatus);

        await fixture.Service.UploadVersionAsync(id, NextVersion(), Bytes("new"), default);

        Assert.Equal(ApprovalStatus.NotSubmitted, (await fixture.Service.GetAsync(id, default)).Value!.ApprovalStatus);
    }

    [Fact]
    public async Task ADocumentPendingApproval_RefusesANewVersion_AndStoresNothing()
    {
        var fixture = Build(ApprovalRequestOutcome.Submitted);
        var id = await UploadAsync(fixture);
        await fixture.Service.SubmitForApprovalAsync(id, default);
        var filesBefore = fixture.Storage.Files.Count;

        var result = await fixture.Service.UploadVersionAsync(id, NextVersion(), Bytes("new"), default);

        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(filesBefore, fixture.Storage.Files.Count);
        Assert.Single(fixture.Repository.Versions);
        Assert.Equal(1, (await fixture.Service.GetAsync(id, default)).Value!.CurrentVersion);
    }

    [Fact]
    public async Task ADocumentThatPredatesHistory_ShowsItsFileAsVersionOne_AndKeepsItWhenAVersionIsAdded()
    {
        var fixture = Build();
        var id = await UploadAsync(fixture, "original");
        fixture.Repository.Versions.Clear(); // what a document uploaded before this feature looks like

        var listed = (await fixture.Service.ListVersionsAsync(id, default)).Value!;
        var only = Assert.Single(listed);
        Assert.Equal(1, only.VersionNumber);
        Assert.True(only.IsCurrent);
        Assert.Null(only.Id);
        Assert.Equal("original", await ReadAsync(await fixture.Service.DownloadVersionAsync(id, 1, default)));

        await fixture.Service.UploadVersionAsync(id, NextVersion(), Bytes("revised"), default);

        // The original file was recorded as version 1 before version 2 replaced it.
        Assert.Equal([1, 2], fixture.Repository.Versions.Select(v => v.VersionNumber));
        Assert.Equal("original", await ReadAsync(await fixture.Service.DownloadVersionAsync(id, 1, default)));
        Assert.Equal("revised", await ReadAsync(await fixture.Service.DownloadVersionAsync(id, 2, default)));
        Assert.Empty(fixture.Storage.Deleted);
    }

    [Fact]
    public async Task DownloadVersion_ReportsUnknownVersionsAndDocuments()
    {
        var fixture = Build();
        var id = await UploadAsync(fixture);

        Assert.Equal("not_found", (await fixture.Service.DownloadVersionAsync(id, 2, default)).Error.Code);
        Assert.Equal("not_found", (await fixture.Service.DownloadVersionAsync(id, 0, default)).Error.Code);
        Assert.Equal("not_found", (await fixture.Service.DownloadVersionAsync(Guid.NewGuid(), 1, default)).Error.Code);
        Assert.Equal("not_found", (await fixture.Service.ListVersionsAsync(Guid.NewGuid(), default)).Error.Code);
        Assert.Equal("not_found", (await fixture.Service.UploadVersionAsync(Guid.NewGuid(), NextVersion(), Bytes("x"), default)).Error.Code);
    }

    [Fact]
    public async Task UploadVersion_RejectsABlankFileName_WithoutStoringAnything()
    {
        var fixture = Build();
        var id = await UploadAsync(fixture);

        var result = await fixture.Service.UploadVersionAsync(id, NextVersion(fileName: "  "), Bytes("x"), default);

        Assert.Equal("validation.error", result.Error.Code);
        Assert.Single(fixture.Storage.Files);
    }

    [Fact]
    public async Task IfSavingFails_TheNewFileIsRemoved_AndTheOriginalIsUntouched()
    {
        var fixture = Build();
        var id = await UploadAsync(fixture, "keep me");
        fixture.UnitOfWork.FailNextSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UploadVersionAsync(id, NextVersion(), Bytes("lost"), default));

        // Only the freshly saved file was cleaned up; the existing one is untouched.
        Assert.Equal(["key-2"], fixture.Storage.Deleted);
        Assert.Equal(new[] { "key-1" }, fixture.Storage.Files.Keys.ToArray());
        Assert.Equal("keep me", Encoding.UTF8.GetString(fixture.Storage.Files["key-1"]));
        // (The fake repository applies changes immediately, whereas a real DbContext only tracks
        // them until SaveChanges - which failed here and whose scope is discarded with the
        // exception. So only storage is asserted: that is the state the failure could corrupt.)
    }

    [Fact]
    public async Task Delete_RemovesEveryVersionsFile_EachExactlyOnce_AndTheirRows()
    {
        var fixture = Build();
        var id = await UploadAsync(fixture);
        await fixture.Service.UploadVersionAsync(id, NextVersion("b.pdf", null), Bytes("b"), default);
        await fixture.Service.UploadVersionAsync(id, NextVersion("c.pdf", null), Bytes("c"), default);

        var result = await fixture.Service.DeleteAsync(id, default);

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Storage.Files);
        Assert.Equal(3, fixture.Storage.Deleted.Count);
        Assert.Equal(fixture.Storage.Deleted.Count, fixture.Storage.Deleted.Distinct().Count());
        Assert.Empty(fixture.Repository.Versions);
        Assert.Empty(fixture.Repository.Documents);
    }

    [Fact]
    public async Task Delete_OfADocumentWithoutVersionRows_StillRemovesItsFile()
    {
        var fixture = Build();
        var id = await UploadAsync(fixture);
        fixture.Repository.Versions.Clear();

        Assert.True((await fixture.Service.DeleteAsync(id, default)).IsSuccess);

        Assert.Empty(fixture.Storage.Files);
        Assert.Equal(["key-1"], fixture.Storage.Deleted);
    }

    // ------------------------------------------------- real EF repository

    [Fact]
    public async Task TheEfRepository_ListsVersionsOldestFirst_ScopedToTheDocument_AndRemovesThem()
    {
        var options = new DbContextOptionsBuilder<ProjectDocumentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var storage = new FakeStorage();
        var documentId = Guid.Empty;
        var otherId = Guid.Empty;

        await using (var db = new ProjectDocumentsDbContext(options))
        {
            var service = new ProjectDocumentService(new ProjectDocumentRepository(db), storage, db, new FakeApprovals());
            documentId = (await service.UploadAsync(new UploadProjectDocumentRequest(Tenant, Project, "A", ProjectDocumentType.Report, "a.pdf", "application/pdf"), Bytes("a1"), default)).Value!.Id;
            otherId = (await service.UploadAsync(new UploadProjectDocumentRequest(Tenant, Project, "B", ProjectDocumentType.Letter, "b.pdf", "application/pdf"), Bytes("b1"), default)).Value!.Id;
            await service.UploadVersionAsync(documentId, NextVersion("a2.pdf", "second"), Bytes("a2"), default);
            await service.UploadVersionAsync(documentId, NextVersion("a3.pdf", "third"), Bytes("a3"), default);
        }

        await using (var db = new ProjectDocumentsDbContext(options))
        {
            var repository = new ProjectDocumentRepository(db);
            Assert.Equal([1, 2, 3], (await repository.ListVersionsAsync(documentId, default)).Select(v => v.VersionNumber));
            Assert.Equal([1], (await repository.ListVersionsAsync(otherId, default)).Select(v => v.VersionNumber));

            var document = (await repository.GetByIdAsync(documentId, default))!;
            Assert.Equal(3, document.CurrentVersion);
            Assert.Equal("a3.pdf", document.FileName);

            var service = new ProjectDocumentService(repository, storage, db, new FakeApprovals());
            Assert.True((await service.DeleteAsync(documentId, default)).IsSuccess);
        }

        await using (var db = new ProjectDocumentsDbContext(options))
        {
            // Only the other document's row and file are left.
            Assert.Equal(1, await db.ProjectDocumentVersions.CountAsync());
            Assert.Equal(1, await db.ProjectDocuments.CountAsync());
            Assert.Single(storage.Files);
        }
    }

    // ------------------------------------------------------------- schema

    [Fact]
    public void TheModel_HasUniqueVersionNumbersPerDocument_AndANonNullIntCurrentVersion()
    {
        using var db = NewSqlServerContext();

        var versions = db.Model.FindEntityType(typeof(ProjectDocumentVersion))!;
        var unique = Assert.Single(versions.GetIndexes(), index => index.IsUnique);
        Assert.Equal(["DocumentId", "VersionNumber"], unique.Properties.Select(p => p.Name));

        var current = db.Model.FindEntityType(typeof(ProjectDocument))!.FindProperty(nameof(ProjectDocument.CurrentVersion))!;
        Assert.Equal("int", current.GetColumnType());
        Assert.False(current.IsNullable);
        // No DB-side default in the model: the column's DEFAULT 1 exists only to number rows that
        // predate it, and EF must always write the real value itself.
        Assert.Null(current.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Null(current.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
    }

    [Fact]
    public void TheUpgradeScriptAndHelper_MatchTheDdlEfGeneratesForTheModel()
    {
        using var db = NewSqlServerContext();
        var generated = db.Database.GenerateCreateScript();

        var versionTable = Regex.Match(generated, @"CREATE TABLE \[project_documents\]\.\[ProjectDocumentVersions\] \((?<body>.*?)\r?\n\);", RegexOptions.Singleline);
        Assert.True(versionTable.Success, "EF no longer generates the ProjectDocumentVersions table the upgrade script assumes.");
        var expected = versionTable.Groups["body"].Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimEnd(','))
            .Where(line => line.Length > 0)
            .ToList();
        Assert.Contains("[VersionNumber] int NOT NULL", expected);

        // The new column, exactly as EF would declare it on a fresh table.
        var documentTable = Regex.Match(generated, @"CREATE TABLE \[project_documents\]\.\[ProjectDocuments\] \((?<body>.*?)\r?\n\);", RegexOptions.Singleline).Groups["body"].Value;
        Assert.Contains("[CurrentVersion] int NOT NULL", documentTable);

        var script = Normalise(File.ReadAllText(Path.Combine(FindSolutionRoot(), "docs", "upgrade", "2026-10-03-add-document-versions.sql")));
        var helper = Normalise(string.Join("\n", (string[])typeof(DocumentsSchemaUpgrade)
            .GetField("Statements", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!));

        foreach (var line in expected)
        {
            Assert.Contains(Normalise(line), script);
            Assert.Contains(Normalise(line), helper);
        }

        foreach (var text in new[] { "[CurrentVersion] int NOT NULL", "DEFAULT 1", "IX_ProjectDocumentVersions_DocumentId_VersionNumber", "CREATE UNIQUE INDEX" })
        {
            Assert.Contains(Normalise(text), script);
            Assert.Contains(Normalise(text), helper);
        }

        Assert.Contains("[DocumentId], [VersionNumber]", generated);
    }

    [Fact]
    public void TheUpgrade_BackfillsVersionOne_AndOnlyForDocumentsWithoutVersions()
    {
        var script = Normalise(File.ReadAllText(Path.Combine(FindSolutionRoot(), "docs", "upgrade", "2026-10-03-add-document-versions.sql")));
        var helper = Normalise(string.Join("\n", (string[])typeof(DocumentsSchemaUpgrade)
            .GetField("Statements", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!));

        foreach (var text in new[] { "SELECT NEWID(), d.[TenantId], d.[Id], 1, d.[StorageKey]", "WHERE NOT EXISTS (SELECT 1 FROM [project_documents].[ProjectDocumentVersions] AS v WHERE v.[DocumentId] = d.[Id])" })
        {
            Assert.Contains(Normalise(text), script);
            Assert.Contains(Normalise(text), helper);
        }
    }

    private static ProjectDocumentsDbContext NewSqlServerContext() => new(
        new DbContextOptionsBuilder<ProjectDocumentsDbContext>()
            .UseSqlServer("Server=.;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);

    private static string Normalise(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NexusCore.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("NexusCore.sln not found above the test output.");
    }
}
