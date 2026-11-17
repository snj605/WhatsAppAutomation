using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Infrastructure.Persistence;

namespace WhatsAppAutomation.Infrastructure.Identity;

/// <summary>
/// Holds the "reset" switch for cached permissions. One instance for the whole app (singleton):
/// cancelling its token makes every cached entry expire at once.
/// </summary>
internal sealed class PermissionCacheSignal
{
    private CancellationTokenSource _source = new();

    public IChangeToken Token => new CancellationChangeToken(_source.Token);

    public void Reset()
    {
        var old = Interlocked.Exchange(ref _source, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }
}

internal sealed class PermissionService(AppDbContext db, IMemoryCache cache, PermissionCacheSignal signal) : IPermissionService
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();
    private static readonly IReadOnlySet<string> Everything = Permissions.All.Select(p => p.Name).ToHashSet();

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = $"permissions:{userId}";
        if (cache.TryGetValue(key, out IReadOnlySet<string>? cached) && cached is not null)
        {
            return cached;
        }

        var token = signal.Token;
        var permissions = await LoadAsync(userId, cancellationToken);

        cache.Set(key, permissions, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            ExpirationTokens = { token },
        });
        return permissions;
    }

    public void InvalidateAll() => signal.Reset();

    private async Task<IReadOnlySet<string>> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null || !user.IsActive)
        {
            return None;
        }

        var roles = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            select new { role.Id, role.IsSystem }).ToListAsync(cancellationToken);

        if (roles.Any(r => r.IsSystem))
        {
            return Everything;
        }

        var roleIds = roles.Select(r => r.Id).ToList();
        var granted = await db.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission)
            .Distinct()
            .ToListAsync(cancellationToken);

        return granted.ToHashSet();
    }
}
