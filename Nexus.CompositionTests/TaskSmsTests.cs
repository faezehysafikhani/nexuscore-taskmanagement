using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Permissions;
using NexusCore.Infrastructure.Persistence;

namespace Nexus.CompositionTests;

/// <summary>
/// The task SMS of the product, through the real HTTP endpoints and the platform's SMS panel
/// (to a recording gateway): only the person responsible for a task is texted - once - with the
/// organization's own wording, and only within their organization. Also the panel's template
/// list and its validation.
/// </summary>
public sealed class TaskSmsTests(AccessControlTests.Host host) : IClassFixture<AccessControlTests.Host>
{
    private static readonly string[] Permissions =
    [
        TaskManagementPermissions.View, TaskManagementPermissions.Create, TaskManagementPermissions.Edit,
        TaskManagementPermissions.Assign
    ];

    private static int _phoneSerial;

    [Fact]
    public async Task ANewTask_TextsOnlyTheResponsiblePerson_NotTheCreatorOrCollaboratorsOrTeam()
    {
        await EnableSmsAsync();
        var (creator, creatorPhone) = await UserWithPhoneAsync(Permissions);
        var (assignee, assigneePhone) = await UserWithPhoneAsync([TaskManagementPermissions.View]);
        var (collaborator, collaboratorPhone) = await UserWithPhoneAsync([TaskManagementPermissions.View]);
        var (member, memberPhone) = await UserWithPhoneAsync([TaskManagementPermissions.View]);
        var team = await host.TeamAsync(host.TenantA, member.Id);

        var response = await host.SendAsync(creator, HttpMethod.Post, "/api/task-management/tasks", new
        {
            title = "Quarterly report", dueDate = "2030-01-01", dueTime = "10:30:00", priority = "Medium",
            assignedUserId = assignee.Id, assignedUserGroupId = team, assigneeUserIds = new[] { collaborator.Id },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var text = Assert.Single(await WaitForAsync(assigneePhone));
        Assert.Equal("فعالیت جدیدی با عنوان «Quarterly report» به شما ارجاع شد.\nموعد انجام: ۱۴۰۸/۱۰/۱۲ - ۱۰:۳۰", text);
        await SettleAsync();
        Assert.Empty(host.Sms.SentTo(creatorPhone));
        Assert.Empty(host.Sms.SentTo(collaboratorPhone));
        Assert.Empty(host.Sms.SentTo(memberPhone));
    }

    [Fact]
    public async Task ACreatorResponsibleForTheirOwnTask_GetsOneSms()
    {
        await EnableSmsAsync();
        var (creator, phone) = await UserWithPhoneAsync(Permissions);

        await host.CreatedTaskIdAsync(creator, "My own", assignedUserId: creator.Id);

        Assert.Single(await WaitForAsync(phone));
        await SettleAsync();
        Assert.Single(host.Sms.SentTo(phone));
    }

    [Fact]
    public async Task NoSms_ForADisabledUser_OrOneWhoTurnedSmsOff_OrHasNoMobile()
    {
        await EnableSmsAsync();
        var (creator, _) = await UserWithPhoneAsync(Permissions);
        var (optedOut, optedOutPhone) = await UserWithPhoneAsync([TaskManagementPermissions.View], notifySms: false);
        var (withoutPhone, _) = await UserWithPhoneAsync([TaskManagementPermissions.View], phone: false);
        var (disabled, disabledPhone) = await UserWithPhoneAsync([TaskManagementPermissions.View]);
        var taskId = await host.CreatedTaskIdAsync(creator, "Before leaving", assignedUserId: creator.Id);
        await SetActiveAsync(disabled.Id, false);

        await host.CreatedTaskIdAsync(creator, "Opted out", assignedUserId: optedOut.Id);
        await host.CreatedTaskIdAsync(creator, "No mobile", assignedUserId: withoutPhone.Id);
        // Whatever the assignment endpoint makes of a disabled user, they are never texted.
        await host.SendAsync(creator, HttpMethod.Patch, $"/api/task-management/tasks/{taskId}/assigned-user", new { assignedUserId = disabled.Id });

        await SettleAsync();
        Assert.Empty(host.Sms.SentTo(optedOutPhone));
        Assert.Empty(host.Sms.SentTo(disabledPhone));
        Assert.DoesNotContain(host.Sms.Sent, m => m.Text.Contains("No mobile"));
    }

    [Fact]
    public async Task AnotherOrganization_WithSmsOff_TextsNobody()
    {
        await EnableSmsAsync();
        var (creator, phone) = await UserWithPhoneAsync(Permissions, tenant: host.TenantB);

        await host.CreatedTaskIdAsync(creator, "Organization B's", assignedUserId: creator.Id);

        await SettleAsync();
        Assert.Empty(host.Sms.SentTo(phone));
        Assert.DoesNotContain(host.Sms.Sent, m => m.Text.Contains("Organization B's"));
    }

    [Fact]
    public async Task HandingATaskToSomeoneElse_TextsOnlyTheNewResponsiblePerson()
    {
        await EnableSmsAsync();
        var (owner, ownerPhone) = await UserWithPhoneAsync(Permissions);
        var (first, firstPhone) = await UserWithPhoneAsync([TaskManagementPermissions.View]);
        var (second, secondPhone) = await UserWithPhoneAsync([TaskManagementPermissions.View]);
        var (third, thirdPhone) = await UserWithPhoneAsync([TaskManagementPermissions.View]);
        var taskId = await host.CreatedTaskIdAsync(owner, "Handover", assignedUserId: first.Id);
        Assert.Single(await WaitForAsync(firstPhone));

        // Through the edit form...
        var edit = await host.SendAsync(owner, HttpMethod.Put, $"/api/task-management/tasks/{taskId}", new
        {
            title = "Handover", dueDate = "2030-01-01", priority = "Medium", assignedUserId = second.Id,
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Contains("«Handover» به شما ارجاع شد", Assert.Single(await WaitForAsync(secondPhone)));

        // ...and through the assignment endpoint.
        var assign = await host.SendAsync(owner, HttpMethod.Patch, $"/api/task-management/tasks/{taskId}/assigned-user", new { assignedUserId = third.Id });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        Assert.Single(await WaitForAsync(thirdPhone));

        // Taking it back themselves: the owner is not texted about their own action.
        var takeBack = await host.SendAsync(owner, HttpMethod.Patch, $"/api/task-management/tasks/{taskId}/assigned-user", new { assignedUserId = owner.Id });
        Assert.Equal(HttpStatusCode.OK, takeBack.StatusCode);

        await SettleAsync();
        Assert.Single(host.Sms.SentTo(firstPhone));
        Assert.Single(host.Sms.SentTo(secondPhone));
        Assert.Single(host.Sms.SentTo(thirdPhone));
        Assert.Empty(host.Sms.SentTo(ownerPhone));
    }

    [Fact]
    public async Task MovingTheDueDate_TextsTheResponsiblePerson_OnlyWhenItReallyMoved()
    {
        await EnableSmsAsync();
        var (owner, ownerPhone) = await UserWithPhoneAsync(Permissions);
        var (assignee, phone) = await UserWithPhoneAsync([TaskManagementPermissions.View]);
        var taskId = await host.CreatedTaskIdAsync(owner, "Budget", assignedUserId: assignee.Id);
        Assert.Single(await WaitForAsync(phone));

        // Another field only: no SMS.
        Assert.Equal(HttpStatusCode.OK, (await EditAsync(owner, taskId, "Budget 2030", "2030-01-01", assignee.Id)).StatusCode);
        await SettleAsync();
        Assert.Single(host.Sms.SentTo(phone));

        // The due date moved: one SMS with the new date.
        Assert.Equal(HttpStatusCode.OK, (await EditAsync(owner, taskId, "Budget 2030", "2030-01-02", assignee.Id)).StatusCode);
        var messages = await WaitForAsync(phone, count: 2);
        Assert.Equal("موعد فعالیت «Budget 2030» تغییر کرد.\nموعد جدید: ۱۴۰۸/۱۰/۱۳", messages[1]);

        // The owner moving the date of their own task is not texted about it.
        var ownTask = await host.CreatedTaskIdAsync(owner, "Own", assignedUserId: owner.Id);
        Assert.Single(await WaitForAsync(ownerPhone));
        Assert.Equal(HttpStatusCode.OK, (await EditAsync(owner, ownTask, "Own", "2030-02-01", owner.Id)).StatusCode);

        await SettleAsync();
        Assert.Equal(2, host.Sms.SentTo(phone).Count);
        Assert.Single(host.Sms.SentTo(ownerPhone));
    }

    [Fact]
    public async Task ThePanel_ListsOnlyTheProductsTemplates_WithTheirPlaceholders()
    {
        var templates = (await TemplatesAsync()).EnumerateArray().ToList();

        Assert.Equal(
            ["password_reset", "task_assigned", "recurring_task_reminder", "task_due_changed"],
            templates.Select(t => t.GetProperty("key").GetString()).OrderBy(Order).ToArray());
        var assigned = templates.Single(t => t.GetProperty("key").GetString() == "task_assigned");
        Assert.Equal("ارجاع فعالیت", assigned.GetProperty("title").GetString());
        Assert.Equal(["TaskTitle", "DueDate", "CreatorName"], assigned.GetProperty("placeholders").EnumerateArray().Select(p => p.GetString()).ToArray());
        Assert.Equal(["TaskTitle"], assigned.GetProperty("requiredPlaceholders").EnumerateArray().Select(p => p.GetString()).ToArray());
        Assert.Equal(assigned.GetProperty("defaultText").GetString(), assigned.GetProperty("text").GetString());
        Assert.False(string.IsNullOrWhiteSpace(assigned.GetProperty("description").GetString()));

        var reset = templates.Single(t => t.GetProperty("key").GetString() == "password_reset");
        Assert.Equal("کد بازیابی رمز عبور شما: {Code}\nاین کد تا {ExpireMinutes} دقیقه معتبر است.", reset.GetProperty("text").GetString());
    }

    [Fact]
    public async Task AnEditedTemplate_IsSavedReloadedAndUsed_AndBrokenOnesAreRefused()
    {
        await EnableSmsAsync();
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveTemplateAsync("task_assigned", "   ")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveTemplateAsync("task_assigned", "کار {TaskTitle} برای {Recipient}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveTemplateAsync("task_assigned", "کار جدید تا {DueDate}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveTemplateAsync("password_reset", "رمز جدید در راه است")).StatusCode);
            // The previous product's texts stay in the core but are not this product's to edit.
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveTemplateAsync("referral", "{title}")).StatusCode);

            var saved = await SaveTemplateAsync("task_assigned", "{CreatorName}: «{TaskTitle}» با شماست تا {DueDate}");
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var reloaded = (await TemplatesAsync()).EnumerateArray().Single(t => t.GetProperty("key").GetString() == "task_assigned");
            Assert.Equal("{CreatorName}: «{TaskTitle}» با شماست تا {DueDate}", reloaded.GetProperty("text").GetString());

            var (creator, _) = await UserWithPhoneAsync(Permissions);
            var (assignee, phone) = await UserWithPhoneAsync([TaskManagementPermissions.View]);
            await host.CreatedTaskIdAsync(creator, "Edited", assignedUserId: assignee.Id);
            var text = Assert.Single(await WaitForAsync(phone));
            Assert.StartsWith(await DisplayNameAsync(creator.Id) + ": «Edited» با شماست تا ۱۴۰۸/۱۰/۱۲", text);
        }
        finally
        {
            var defaults = (await TemplatesAsync()).EnumerateArray().Single(t => t.GetProperty("key").GetString() == "task_assigned");
            Assert.Equal(HttpStatusCode.OK, (await SaveTemplateAsync("task_assigned", defaults.GetProperty("defaultText").GetString()!)).StatusCode);
        }
    }

    private static int Order(string? key) => Array.IndexOf(["password_reset", "task_assigned", "recurring_task_reminder", "task_due_changed"], key);

    private Task<HttpResponseMessage> EditAsync(AccessControlTests.Caller caller, Guid taskId, string title, string dueDate, Guid assignee) =>
        host.SendAsync(caller, HttpMethod.Put, $"/api/task-management/tasks/{taskId}", new { title, dueDate, priority = "Medium", assignedUserId = assignee });

    private async Task EnableSmsAsync()
    {
        var response = await host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/notification-channels", new
        {
            sms = new { enabled = true, provider = "test", apiKey = "test-key", lineNumber = "1000092120" },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> TemplatesAsync()
    {
        var response = await host.SendAsync(host.Admin, HttpMethod.Get, "/api/platform/notification-channels/templates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private Task<HttpResponseMessage> SaveTemplateAsync(string key, string text) =>
        host.SendAsync(host.Admin, HttpMethod.Put, "/api/platform/notification-channels/templates", new { templates = new[] { new { key, text } } });

    private async Task<(AccessControlTests.Caller Caller, string Phone)> UserWithPhoneAsync(
        IEnumerable<string> permissions, Guid? tenant = null, bool notifySms = true, bool phone = true)
    {
        var caller = await host.UserAsync(tenant ?? host.TenantA, permissions);
        var number = $"0912{Interlocked.Increment(ref _phoneSerial) + 1000000:D7}";
        using var scope = host.Services.CreateScope();
        var core = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
        var user = await core.Users.SingleAsync(u => u.Id == caller.Id);
        user.UpdateContactDetails(user.Username, phone ? number : null, notifySms);
        await core.SaveChangesAsync();
        return (caller, number);
    }

    private async Task SetActiveAsync(Guid userId, bool active)
    {
        using var scope = host.Services.CreateScope();
        var core = scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>();
        (await core.Users.SingleAsync(u => u.Id == userId)).SetActive(active);
        await core.SaveChangesAsync();
        var tasks = scope.ServiceProvider.GetRequiredService<Nexus.TaskManagement.Infrastructure.TaskManagementDbContext>();
        (await tasks.Users.SingleAsync(u => u.Id == userId)).SetActive(active);
        await tasks.SaveChangesAsync();
    }

    private async Task<string> DisplayNameAsync(Guid userId)
    {
        using var scope = host.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<NexusCoreDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == userId)).DisplayName;
    }

    /// <summary>The texts sent to this number, once there are at least <paramref name="count"/> (SMS go out in the background).</summary>
    private async Task<IReadOnlyList<string>> WaitForAsync(string phone, int count = 1)
    {
        for (var i = 0; i < 100 && host.Sms.SentTo(phone).Count < count; i++)
        {
            await Task.Delay(50);
        }

        return host.Sms.SentTo(phone).Select(m => m.Text).ToList();
    }

    /// <summary>Gives background SMS that should not exist the time they would have needed.</summary>
    private static Task SettleAsync() => Task.Delay(500);
}
