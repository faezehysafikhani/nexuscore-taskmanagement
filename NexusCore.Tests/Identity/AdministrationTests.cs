using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Messaging;
using NexusCore.Domain.Identity;

namespace NexusCore.Tests.Identity;

/// <summary>User administration rules that live in the domain and in the permission catalogue.</summary>
public sealed class AdministrationTests
{
    [Fact]
    public void User_SetName_BuildsTheDisplayName()
    {
        var user = new User(Guid.NewGuid(), Guid.NewGuid(), "a@example.com", "old", "hash");

        user.SetName("  علی ", " محمدی ");

        Assert.Equal("علی", user.FirstName);
        Assert.Equal("محمدی", user.LastName);
        Assert.Equal("علی محمدی", user.DisplayName);
    }

    [Fact]
    public void User_IsNotASystemAccountUntilTheSeederMarksIt()
    {
        var user = new User(Guid.NewGuid(), Guid.NewGuid(), "a@example.com", "Admin", "hash");
        Assert.False(user.IsSystem);

        user.MarkAsSystemAccount();

        Assert.True(user.IsSystem);
    }

    [Fact]
    public void User_SetDirectPermissions_ReplacesAndDeduplicates()
    {
        var user = new User(Guid.NewGuid(), Guid.NewGuid(), "a@example.com", "User", "hash");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        user.SetDirectPermissions([first]);
        user.SetDirectPermissions([second, second]);

        Assert.Equal(second, Assert.Single(user.Permissions).PermissionId);
    }

    [Fact]
    public void AdministrationPermissions_AreInTheCatalogue()
    {
        var names = IdentityPermissions.All.Select(p => p.Name).ToHashSet();
        foreach (var name in new[]
                 {
                     "users.change_status", "users.assign_permissions",
                     "sms_settings.view", "sms_settings.update", "sms_settings.test",
                     "ldap_settings.view", "ldap_settings.update", "ldap_settings.test",
                     "sso_settings.view",
                 })
        {
            Assert.Contains(name, names);
        }
    }

    [Fact]
    public void SmsTemplates_CoverCoresSystemMessages_AndUseOnlyDeclaredPlaceholders()
    {
        var definitions = new CoreSmsTemplateCatalog().GetTemplates();
        Assert.Equal(
            [SmsTemplateKeys.PasswordReset, SmsTemplateKeys.Letter, SmsTemplateKeys.Referral, SmsTemplateKeys.MeetingNotice, SmsTemplateKeys.ProposalRejected],
            definitions.Select(d => d.Key).ToArray());

        foreach (var definition in definitions)
        {
            var used = System.Text.RegularExpressions.Regex.Matches(definition.DefaultText, @"\{([A-Za-z][A-Za-z0-9_]*)\}").Select(m => m.Groups[1].Value).ToList();
            Assert.All(used, placeholder => Assert.Contains(placeholder, definition.Placeholders));
            Assert.All(definition.RequiredPlaceholders ?? [], required => Assert.Contains(required, used));
            Assert.True(definition.DefaultText.Length <= SmsTemplateService.MaxTextLength);
        }
    }
}
