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

    /// <summary>
    /// From the kind of user and, for members, their switches. Nothing for a disabled user, a user of a
    /// turned-off tenant, or a user who must first choose their own password.
    /// </summary>
    private async Task<IReadOnlySet<string>> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.IsActive,
                u.Kind,
                u.MustChangePassword,
                TenantActive = u.TenantId == null || db.Tenants.Any(t => t.Id == u.TenantId && t.IsActive),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null || !user.IsActive || !user.TenantActive || user.MustChangePassword)
        {
            return None;
        }

        var switches = user.Kind == UserKind.Member
            ? await db.UserSwitches.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.Permission).ToListAsync(cancellationToken)
            : [];
        return UserKinds.For(user.Kind, switches);
    }
}
