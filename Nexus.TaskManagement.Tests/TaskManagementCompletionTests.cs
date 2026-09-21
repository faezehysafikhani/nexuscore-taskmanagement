using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Application.Dtos;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure;

namespace Nexus.TaskManagement.Tests;

/// <summary>
/// File contents, comment attachments, generated occurrences and UI history entries, against
/// real SQL Server and real disk storage. The first test is the one that was missing when file
/// uploads stored only metadata: it reads the bytes back.
/// </summary>
[Collection("sqlserver")]
public sealed class TaskManagementCompletionTests(SqlServerFixture fixture)
{
    private bool Skip => !fixture.Available;

    private static CreateTaskRequest PlainTask(string title) =>
        new(title, new DateOnly(2026, 10, 1));

    [Fact]
    public async Task UploadedFileContentComesBackOnDownload_AndIsGoneAfterDelete()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Has content"), default));
        var bytes = Enumerable.Range(0, 1000).Select(i => (byte)(i % 251)).ToArray();

        var upload = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskFileService>()
            .UploadToTaskAsync(task.Value!.Id, new UploadFileRequest("data.bin", "application/octet-stream", bytes), default));
        Assert.True(upload.IsSuccess, upload.IsFailure ? upload.Error.Message : null);

        var download = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskFileService>().DownloadAsync(upload.Value!.FileId, default));
        Assert.True(download.IsSuccess, download.IsFailure ? download.Error.Message : null);
        Assert.Equal(bytes, download.Value!.Content);
        Assert.Equal("data.bin", download.Value.FileName);

        var storageKey = await fixture.ScopedAsync(sp => sp.GetRequiredService<TaskManagementDbContext>()
            .Files.Where(f => f.Id == upload.Value!.FileId).Select(f => f.StoragePath).SingleAsync());
        Assert.True(File.Exists(Path.Combine(fixture.StorageRoot, storageKey)));

        var deleted = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskFileService>().DeleteAsync(upload.Value!.LinkId, default));
        Assert.True(deleted.IsSuccess);
        Assert.False(File.Exists(Path.Combine(fixture.StorageRoot, storageKey)));
    }

    [Fact]
    public async Task CommentAttachmentBelongsToTheComment_AndOnlyItsAuthorCanAdd()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Discussed with files"), default));
        var comment = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskCommentService>()
            .CreateAsync(task.Value!.Id, new CreateTaskCommentRequest("See attached"), default), fixture.OwnerUserId);
        Assert.Equal("Owner", comment.Value!.UserDisplayName);

        var mine = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskFileService>()
            .UploadToCommentAsync(comment.Value!.Id, new UploadFileRequest("a.txt", "text/plain", new byte[10]), default), fixture.OwnerUserId);
        Assert.True(mine.IsSuccess);

        var someoneElse = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskFileService>()
            .UploadToCommentAsync(comment.Value!.Id, new UploadFileRequest("b.txt", "text/plain", new byte[10]), default), fixture.OtherUserId);
        Assert.True(someoneElse.IsFailure);
        Assert.Equal("forbidden", someoneElse.Error.Code);

        await fixture.ScopedAsync(async sp =>
        {
            var link = await sp.GetRequiredService<TaskManagementDbContext>().TaskFiles.SingleAsync(f => f.Id == mine.Value!.LinkId);
            Assert.Equal(comment.Value!.Id, link.CommentId);
            Assert.Null(link.TaskId);
            Assert.Null(link.SubTaskId);
        });

        var listed = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskCommentService>().ListAsync(task.Value!.Id, default));
        var files = Assert.Single(listed.Value!).Files;
        Assert.NotNull(files);
        Assert.Equal("a.txt", Assert.Single(files!).FileName);

        // Deleting the comment takes its attachment with it - row and content.
        var removed = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskCommentService>().DeleteAsync(comment.Value!.Id, default), fixture.OwnerUserId);
        Assert.True(removed.IsSuccess);
        var remaining = await fixture.ScopedAsync(sp => sp.GetRequiredService<TaskManagementDbContext>().Files.CountAsync(f => f.Id == mine.Value!.FileId));
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task EditingSomeoneElsesComment_IsForbiddenNotUnauthorized()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Guarded comment"), default));
        var comment = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskCommentService>()
            .CreateAsync(task.Value!.Id, new CreateTaskCommentRequest("Mine"), default), fixture.OwnerUserId);

        var edit = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskCommentService>()
            .UpdateAsync(comment.Value!.Id, new UpdateTaskCommentRequest("Not yours"), default), fixture.OtherUserId);
        var delete = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskCommentService>()
            .DeleteAsync(comment.Value!.Id, default), fixture.OtherUserId);

        // 403, not 401: a 401 would make the client throw away a perfectly valid session.
        Assert.Equal("forbidden", edit.Error.Code);
        Assert.Equal("forbidden", delete.Error.Code);
    }

    [Fact]
    public async Task DeletingATask_RemovesTaskAndCommentFilesFromStorage()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Goes with its files"), default));
        var taskFile = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskFileService>()
            .UploadToTaskAsync(task.Value!.Id, new UploadFileRequest("t.txt", "text/plain", new byte[5]), default));
        var comment = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskCommentService>()
            .CreateAsync(task.Value!.Id, new CreateTaskCommentRequest("with file"), default));
        var commentFile = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskFileService>()
            .UploadToCommentAsync(comment.Value!.Id, new UploadFileRequest("c.txt", "text/plain", new byte[5]), default));

        var keys = await fixture.ScopedAsync(sp => sp.GetRequiredService<TaskManagementDbContext>().Files
            .Where(f => f.Id == taskFile.Value!.FileId || f.Id == commentFile.Value!.FileId)
            .Select(f => f.StoragePath).ToListAsync());
        Assert.Equal(2, keys.Count);

        var deleted = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().DeleteAsync(task.Value!.Id, default));
        Assert.True(deleted.IsSuccess, deleted.IsFailure ? deleted.Error.Message : null);

        Assert.All(keys, key => Assert.False(File.Exists(Path.Combine(fixture.StorageRoot, key))));
        var rows = await fixture.ScopedAsync(sp => sp.GetRequiredService<TaskManagementDbContext>().Files
            .CountAsync(f => f.Id == taskFile.Value!.FileId || f.Id == commentFile.Value!.FileId));
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task GeneratedOccurrenceFlag_IsStoredAndReturned()
    {
        if (Skip) return;

        var created = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(
            PlainTask("Weekly") with
            {
                SubTasks =
                [
                    new SubTaskInput("Written by a person"),
                    new SubTaskInput("Occurrence 1", IsGeneratedOccurrence: true)
                ]
            }, default));
        Assert.True(created.IsSuccess);
        Assert.Equal([false, true], created.Value!.SubTasks.OrderBy(s => s.SortOrder).Select(s => s.IsGeneratedOccurrence));

        var added = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>()
            .CreateSubTaskAsync(created.Value!.Id, new CreateSubTaskRequest("Occurrence 2", IsGeneratedOccurrence: true), default));
        Assert.True(added.Value!.IsGeneratedOccurrence);
    }

    [Fact]
    public async Task ActivityEntry_IsRecordedUnderTheCaller_AndValidated()
    {
        if (Skip) return;

        var task = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>().CreateAsync(PlainTask("Has history"), default));

        var ok = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>()
            .AddActivityEntryAsync(task.Value!.Id, new CreateTaskActivityRequest("ویرایش ۲ فیلد", "عنوان | اولویت"), default), fixture.OtherUserId);
        Assert.True(ok.IsSuccess);

        var history = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskActivityService>().GetForTaskAsync(task.Value!.Id, default));
        var entry = Assert.Single(history.Value!, a => a.Action == "ویرایش ۲ فیلد");
        Assert.Equal(fixture.OtherUserId, entry.UserId);
        Assert.Equal("عنوان | اولویت", entry.Details);

        var tooLong = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>()
            .AddActivityEntryAsync(task.Value!.Id, new CreateTaskActivityRequest(new string('x', 121)), default));
        Assert.Equal("validation.error", tooLong.Error.Code);

        var missingTask = await fixture.ScopedAsync(sp => sp.GetRequiredService<ITaskService>()
            .AddActivityEntryAsync(Guid.NewGuid(), new CreateTaskActivityRequest("x"), default));
        Assert.Equal("not_found", missingTask.Error.Code);
    }
}
