using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;
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

        if (!user.IsActive || !await TenantActiveAsync(user.TenantId, cancellationToken))
        {
            return new PasswordCheckResult(PasswordCheckOutcome.Disabled, null);
        }

        return new PasswordCheckResult(PasswordCheckOutcome.Succeeded, await ToSummaryAsync(user, cancellationToken));
    }

    public async Task<UserSummary?> FindActiveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user) ||
            !await TenantActiveAsync(user.TenantId, cancellationToken))
        {
            return null;
        }

        return await ToSummaryAsync(user, cancellationToken);
    }

    public Task<bool> MaySendAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(
            u => u.Id == userId && u.IsActive && u.Kind != UserKind.PlatformAdmin &&
                db.Tenants.Any(t => t.Id == u.TenantId && t.IsActive),
            cancellationToken);

    public async Task<IReadOnlyList<UserDetails>> ListUsersAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        // Two queries in total (users, then all their switches), not one per user.
        var all = await db.Users.AsNoTracking()
            .Where(u => tenantId == null ? u.Kind == UserKind.PlatformAdmin : u.TenantId == tenantId)
            .OrderBy(u => u.DisplayName).ThenBy(u => u.UserName)
            .ToListAsync(cancellationToken);
        var switches = await SwitchesAsync(all.Select(u => u.Id).ToList(), cancellationToken);
        return [.. all.Select(u => ToDetails(u, switches))];
    }

    public async Task<UserDetails?> FindUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : ToDetails(user, await SwitchesAsync([userId], cancellationToken));
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
            Kind = newUser.Kind,
            TenantId = newUser.Kind == UserKind.PlatformAdmin ? null : newUser.TenantId,
            MustChangePassword = newUser.MustChangePassword,
        };

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var created = await users.CreateAsync(user, newUser.Password);
            if (!created.Succeeded)
            {
                return (Result<Guid>)Error.Validation(string.Join(" ", created.Errors.Select(e => e.Description)));
            }

            if (user.Kind == UserKind.Member)
            {
                db.UserSwitches.AddRange(Clean(newUser.Switches).Select(p => new UserSwitch(user.Id, p)));
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Result<Guid>.Success(user.Id);
        });
    }

    public async Task UpdateUserAsync(
        Guid userId, string displayName, UserKind kind, IReadOnlyCollection<string> switches, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(
                s => s.SetProperty(u => u.DisplayName, displayName).SetProperty(u => u.Kind, kind), cancellationToken);
            await db.UserSwitches.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            if (kind == UserKind.Member)
            {
                db.UserSwitches.AddRange(Clean(switches).Select(p => new UserSwitch(userId, p)));
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        });
    }

    public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
        db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, isActive), cancellationToken);

    public Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);

    public Task<int> CountActiveAsync(UserKind kind, Guid? tenantId, Guid? excludingUserId, CancellationToken cancellationToken) =>
        db.Users.CountAsync(
            u => u.Kind == kind && u.IsActive && u.TenantId == tenantId && u.Id != excludingUserId, cancellationToken);

    public async Task<IReadOnlyList<(Guid UserId, Guid? TenantId, string DisplayName)>> UsersByTenantAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Users.AsNoTracking().Where(u => u.Kind != UserKind.PlatformAdmin)
            .Select(u => new { u.Id, u.TenantId, u.DisplayName }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => (r.Id, r.TenantId, r.DisplayName))];
    }

    public async Task<IReadOnlyDictionary<Guid, (int Users, int Admins)>> CountByTenantAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Users.AsNoTracking()
            .Where(u => u.TenantId != null)
            .GroupBy(u => u.TenantId!.Value)
            .Select(g => new { TenantId = g.Key, Users = g.Count(), Admins = g.Count(u => u.Kind == UserKind.TenantAdmin) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.TenantId, r => (r.Users, r.Admins));
    }

    public async Task<Result> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new Error("users.not_found", "This user does not exist.", ErrorKind.NotFound);
        }

        var changed = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!changed.Succeeded)
        {
            return changed.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
                ? Error.Validation("The current password is not right.")
                : Error.Validation(string.Join(" ", changed.Errors.Select(e => e.Description)));
        }

        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new Error("users.not_found", "This user does not exist.", ErrorKind.NotFound);
        }

        // Checked first, so a refused password never leaves the user without one.
        foreach (var validator in users.PasswordValidators)
        {
            var check = await validator.ValidateAsync(users, user, newPassword);
            if (!check.Succeeded)
            {
                return Error.Validation(string.Join(" ", check.Errors.Select(e => e.Description)));
            }
        }

        user.PasswordHash = users.PasswordHasher.HashPassword(user, newPassword);
        await users.UpdateSecurityStampAsync(user);

        user.MustChangePassword = true;
        await users.UpdateAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return Result.Success();
    }

    private static IEnumerable<string> Clean(IEnumerable<string> switches) =>
        switches.Where(UserKinds.MemberSwitches.Contains).Distinct();

    private async Task<bool> TenantActiveAsync(Guid? tenantId, CancellationToken cancellationToken) =>
        tenantId is not { } id || await db.Tenants.AnyAsync(t => t.Id == id && t.IsActive, cancellationToken);

    private async Task<Dictionary<Guid, List<string>>> SwitchesAsync(List<Guid> userIds, CancellationToken cancellationToken)
    {
        var pairs = await db.UserSwitches.AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .Select(s => new { s.UserId, s.Permission })
            .ToListAsync(cancellationToken);
        return pairs.GroupBy(p => p.UserId).ToDictionary(
            g => g.Key, g => g.Select(p => p.Permission).OrderBy(p => UserKinds.MemberSwitches.ToList().IndexOf(p)).ToList());
    }

    private static UserDetails ToDetails(AppUser user, Dictionary<Guid, List<string>> switches) =>
        new(user.Id, user.UserName!, user.DisplayName, user.Kind, user.TenantId, user.IsActive,
            switches.GetValueOrDefault(user.Id) ?? [], user.CreatedAt);

    private async Task<UserSummary> ToSummaryAsync(AppUser user, CancellationToken cancellationToken)
    {
        var tenantName = user.TenantId is { } id
            ? await db.Tenants.AsNoTracking().Where(t => t.Id == id).Select(t => t.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new UserSummary(user.Id, user.UserName!, user.DisplayName, user.Kind, user.TenantId, tenantName, user.MustChangePassword);
    }
}
