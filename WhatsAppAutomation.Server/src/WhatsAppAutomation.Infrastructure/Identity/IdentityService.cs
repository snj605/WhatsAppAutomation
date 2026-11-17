using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Infrastructure.Persistence;

namespace WhatsAppAutomation.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<AppUser> users, AppDbContext db, TimeProvider clock) : IIdentityService
{
    // Verified when the user name is unknown, so both failure paths take about the same time.
    private static readonly Lazy<string> DummyHash =
        new(() => new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString()));

    public async Task<PasswordCheckResult> CheckPasswordAsync(
        string userName, string password, CancellationToken cancellationToken)
    {
        var user = await users.FindByNameAsync(userName);
        if (user is null)
        {
            users.PasswordHasher.VerifyHashedPassword(new AppUser(), DummyHash.Value, password);
            return new PasswordCheckResult(PasswordCheckOutcome.InvalidCredentials, null);
        }

        if (await users.IsLockedOutAsync(user))
        {
            return new PasswordCheckResult(PasswordCheckOutcome.LockedOut, null);
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            var outcome = await users.IsLockedOutAsync(user)
                ? PasswordCheckOutcome.LockedOut
                : PasswordCheckOutcome.InvalidCredentials;
            return new PasswordCheckResult(outcome, null);
        }

        await users.ResetAccessFailedCountAsync(user);

        if (!user.IsActive)
        {
            return new PasswordCheckResult(PasswordCheckOutcome.Disabled, null);
        }

        return new PasswordCheckResult(PasswordCheckOutcome.Succeeded, await ToSummaryAsync(user, cancellationToken));
    }

    public async Task<UserSummary?> FindActiveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user))
        {
            return null;
        }

        return await ToSummaryAsync(user, cancellationToken);
    }

    public async Task<IReadOnlyList<UserDetails>> ListUsersAsync(CancellationToken cancellationToken)
    {
        // Two queries in total (users, then all their roles), not one per user.
        var all = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(cancellationToken);
        var roles = await RolesByUserAsync(all.Select(u => u.Id).ToList(), cancellationToken);
        return [.. all.Select(u => ToDetails(u, roles))];
    }

    public async Task<UserDetails?> FindUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : ToDetails(user, await RolesByUserAsync([userId], cancellationToken));
    }

    public async Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken)
    {
        var normalized = users.NormalizeName(userName.Trim());
        return await db.Users.AnyAsync(u => u.NormalizedUserName == normalized, cancellationToken);
    }

    public async Task<Result<Guid>> CreateUserAsync(NewUser newUser, CancellationToken cancellationToken)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = newUser.UserName,
            DisplayName = newUser.DisplayName,
            CreatedAt = clock.GetUtcNow(),
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var created = await users.CreateAsync(user, newUser.Password);
        if (!created.Succeeded)
        {
            return Error.Validation(string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        db.UserRoles.AddRange(newUser.RoleIds.Select(roleId => new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return user.Id;
    }

    public async Task SetRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.UserRoles.Where(ur => ur.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        db.UserRoles.AddRange(roleIds.Select(roleId => new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
        db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, isActive), cancellationToken);

    public Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);

    public Task<int> CountActiveOwnersAsync(Guid? excludingUserId, CancellationToken cancellationToken) =>
        (from user in db.Users
         join userRole in db.UserRoles on user.Id equals userRole.UserId
         join role in db.Roles on userRole.RoleId equals role.Id
         where role.IsSystem && user.IsActive && user.Id != excludingUserId
         select user.Id).Distinct().CountAsync(cancellationToken);

    private async Task<Dictionary<Guid, List<RoleRef>>> RolesByUserAsync(
        List<Guid> userIds, CancellationToken cancellationToken)
    {
        var pairs = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where userIds.Contains(userRole.UserId)
            orderby role.Name
            select new { userRole.UserId, role.Id, role.Name }).ToListAsync(cancellationToken);

        return pairs.GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.Select(p => new RoleRef(p.Id, p.Name!)).ToList());
    }

    private static UserDetails ToDetails(AppUser user, Dictionary<Guid, List<RoleRef>> roles) =>
        new(user.Id, user.UserName!, user.DisplayName, user.IsActive, roles.GetValueOrDefault(user.Id) ?? [], user.CreatedAt);

    private async Task<UserSummary> ToSummaryAsync(AppUser user, CancellationToken cancellationToken)
    {
        var roles = await RolesByUserAsync([user.Id], cancellationToken);
        return new UserSummary(
            user.Id,
            user.UserName!,
            user.DisplayName,
            [.. (roles.GetValueOrDefault(user.Id) ?? []).Select(r => r.Name)]);
    }
}
