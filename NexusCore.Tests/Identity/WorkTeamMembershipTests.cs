using System.Reflection;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Identity.Services;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Tests.Identity;

/// <summary>People added to a work team are told so; the person adding them and those already in it are not.</summary>
public sealed class WorkTeamMembershipTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly User _owner = new(Guid.NewGuid(), Tenant, "owner@example.com", "امیر محمودی", "hash");
    private readonly User _nima = new(Guid.NewGuid(), Tenant, "nima@example.com", "نیما", "hash");
    private readonly User _sara = new(Guid.NewGuid(), Tenant, "sara@example.com", "سارا", "hash");
    private readonly User _reza = new(Guid.NewGuid(), Tenant, "reza@example.com", "رضا", "hash");
    private readonly FakeGroups _groups = new();
    private readonly RecordingNotifier _notifier = new();

    public WorkTeamMembershipTests()
    {
        _groups.Users.AddRange([_owner, _nima, _sara, _reza]);
    }

    private UserGroupService Service() => new(
        _groups,
        Stub<IIdentityRepository>(),
        new NoOpUnitOfWork(),
        Stub<IPlatformService>(),
        new SignedIn(_owner.Id, Tenant),
        Options.Create(new ManagedPermissionOptions()),
        _notifier);

    [Fact]
    public async Task NewTeamMembers_AreNotified_ButNotTheOwner()
    {
        var service = Service();
        var team = (await service.CreateMyTeamAsync(new CreateMyTeamRequest("تیم طراحی", null), default)).Value!;

        Assert.True((await service.SetMyTeamMembersAsync(team.Id, new SetMyTeamMembersRequest([_nima.Id, _sara.Id]), default)).IsSuccess);

        var change = Assert.Single(_notifier.Changes);
        Assert.Equal(new[] { _nima.Id, _sara.Id }.Order(), change.UserIds.Order());
        Assert.Equal("تیم طراحی", change.GroupName);
        Assert.Equal("امیر محمودی", change.AddedByName);
        Assert.True(change.IsPersonalTeam);
        Assert.Equal(Tenant, change.TenantId);
    }

    [Fact]
    public async Task OnlyPeopleWhoWereNotMembersBefore_AreNotified()
    {
        var service = Service();
        var team = (await service.CreateMyTeamAsync(new CreateMyTeamRequest("تیم مالی", null), default)).Value!;
        await service.SetMyTeamMembersAsync(team.Id, new SetMyTeamMembersRequest([_nima.Id]), default);

        await service.SetMyTeamMembersAsync(team.Id, new SetMyTeamMembersRequest([_nima.Id, _reza.Id]), default);
        await service.SetMyTeamMembersAsync(team.Id, new SetMyTeamMembersRequest([_reza.Id]), default);

        Assert.Equal(2, _notifier.Changes.Count);
        Assert.Equal([_reza.Id], _notifier.Changes[1].UserIds);
    }

    [Fact]
    public void ManagingOwnTeams_ImpliesSeeingTheSection()
    {
        Assert.Contains(UserGroupPermissions.GroupsViewOwn, PermissionPrerequisites.Expand([UserGroupPermissions.GroupsManageOwn]));
        Assert.Contains(UserGroupPermissions.All, p => p.Name == "groups.view_own");
    }

    private sealed class RecordingNotifier : IUserGroupMembershipNotifier
    {
        public List<UserGroupMembersAdded> Changes { get; } = [];

        public Task MembersAddedAsync(UserGroupMembersAdded change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGroups : IUserGroupRepository
    {
        private readonly List<UserGroup> _groups = [];
        public List<User> Users { get; } = [];

        public Task<IReadOnlyList<UserGroup>> ListAsync(Guid? tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserGroup>>(_groups.Where(g => g.TenantId == tenantId).ToList());
        public Task<UserGroup?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken) =>
            Task.FromResult(_groups.SingleOrDefault(g => g.Id == groupId));
        public Task<bool> NameExistsAsync(Guid tenantId, string normalizedName, Guid? excludeGroupId, CancellationToken cancellationToken, Guid? ownerUserId = null) =>
            Task.FromResult(_groups.Any(g => g.TenantId == tenantId && g.OwnerUserId == ownerUserId && g.NormalizedName == normalizedName && g.Id != excludeGroupId));
        public Task<IReadOnlyList<UserGroup>> ListOwnedAsync(Guid tenantId, Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserGroup>>(_groups.Where(g => g.OwnerUserId == ownerUserId).ToList());
        public Task<IReadOnlyList<UserGroup>> ListVisibleToUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserGroup>>(_groups.Where(g => g.Members.Any(m => m.UserId == userId)).ToList());
        public void Remove(UserGroup group) => _groups.Remove(group);
        public Task AddAsync(UserGroup group, CancellationToken cancellationToken)
        {
            _groups.Add(group);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<User>> ListUsersAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<User>>(Users.Where(u => userIds.Contains(u.Id)).ToList());
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class SignedIn(Guid userId, Guid tenantId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public Guid? TenantId => tenantId;
        public string? Email => null;
        public string? IpAddress => null;
    }

    /// <summary>An implementation whose methods do nothing (completed tasks, default values).</summary>
    private static T Stub<T>() where T : class => DispatchProxy.Create<T, StubProxy>();

    public class StubProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var type = targetMethod!.ReturnType;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var result = type.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result)
                    .Invoke(null, [result.IsValueType ? Activator.CreateInstance(result) : null]);
            }
            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }
}
