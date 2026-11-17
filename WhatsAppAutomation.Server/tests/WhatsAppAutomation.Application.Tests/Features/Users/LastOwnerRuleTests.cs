using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Users;
using WhatsAppAutomation.Application.Features.Users.Commands;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Tests.Features.Users;

/// <summary>
/// Unit tests: the handlers run with hand-written fakes instead of a database, so we can set up
/// "this is the only Owner" exactly. The API tests share one database with many Owners.
/// </summary>
public sealed class LastOwnerRuleTests
{
    private static readonly RoleDetails OwnerRole = new(Guid.NewGuid(), SystemRoles.Owner, null, true, [], 1);
    private static readonly RoleDetails SenderRole = new(Guid.NewGuid(), SystemRoles.Sender, null, false, [], 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_last_active_Owner_cannot_lose_the_Owner_role()
    {
        var identity = new FakeIdentity(otherActiveOwners: 0);
        var handler = new SetUserRolesCommandHandler(identity, new FakeRoles(), new FakePermissions());

        var result = await handler.Handle(new SetUserRolesCommand(identity.User.Id, [SenderRole.Id]), Ct);

        Assert.Equal(UserErrors.LastOwner, result.Error);
        Assert.False(identity.RolesChanged);
    }

    [Fact]
    public async Task An_Owner_can_lose_the_role_when_another_Owner_exists()
    {
        var identity = new FakeIdentity(otherActiveOwners: 1);
        var handler = new SetUserRolesCommandHandler(identity, new FakeRoles(), new FakePermissions());

        var result = await handler.Handle(new SetUserRolesCommand(identity.User.Id, [SenderRole.Id]), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(identity.RolesChanged);
    }

    [Fact]
    public async Task The_last_active_Owner_cannot_be_disabled()
    {
        var identity = new FakeIdentity(otherActiveOwners: 0);
        var handler = new SetUserActiveCommandHandler(
            identity, new FakeCurrentUser(Guid.NewGuid()), null!, new FakePermissions(), TimeProvider.System);

        var result = await handler.Handle(new SetUserActiveCommand(identity.User.Id, false), Ct);

        Assert.Equal(UserErrors.LastOwner, result.Error);
    }

    private sealed class FakeIdentity(int otherActiveOwners) : IIdentityService
    {
        public UserDetails User { get; } = new(
            Guid.NewGuid(), "owner", "Owner", true, [new RoleRef(OwnerRole.Id, OwnerRole.Name)], DateTimeOffset.UtcNow);

        public bool RolesChanged { get; private set; }

        public Task<UserDetails?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<UserDetails?>(userId == User.Id ? User : null);

        public Task<int> CountActiveOwnersAsync(Guid? excludingUserId, CancellationToken cancellationToken) =>
            Task.FromResult(otherActiveOwners);

        public Task SetRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
        {
            RolesChanged = true;
            return Task.CompletedTask;
        }

        public Task<PasswordCheckResult> CheckPasswordAsync(string userName, string password, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserSummary?> FindActiveUserAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<UserDetails>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<Guid>> CreateUserAsync(NewUser user, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeRoles : IRoleService
    {
        public Task<IReadOnlyList<RoleDetails>> FindManyAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoleDetails>>([.. new[] { OwnerRole, SenderRole }.Where(r => roleIds.Contains(r.Id))]);

        public Task<IReadOnlyList<RoleDetails>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RoleDetails?> FindAsync(Guid roleId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> NameExistsAsync(string name, Guid? exceptRoleId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> CreateAsync(string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(Guid roleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePermissions : IPermissionService
    {
        public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void InvalidateAll()
        {
        }
    }

    private sealed class FakeCurrentUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }
}
