using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Authentication;

public sealed record AuthSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserSummary User);

/// <summary>Creates and ends sessions (refresh tokens). Shared by the sign-in and user handlers.</summary>
internal sealed class SessionStore(IAppDbContext db, IAccessTokenIssuer accessTokens, IOptions<AuthOptions> options)
{
    public async Task<AuthSession> IssueAsync(
        UserSummary user,
        Guid familyId,
        string deviceId,
        string? deviceName,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        Guid? tokenId = null)
    {
        var secret = RefreshTokenSecret.Create();
        var refresh = new RefreshToken(
            user.Id,
            RefreshTokenSecret.Hash(secret),
            familyId,
            deviceId,
            deviceName,
            now,
            now.Add(options.Value.RefreshTokenLifetime),
            tokenId);

        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(cancellationToken);

        var access = accessTokens.Issue(user, deviceId);
        return new AuthSession(access.Token, access.ExpiresAt, secret, refresh.ExpiresAt, user);
    }

    public Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        RevokeActiveAsync(db.RefreshTokens.Where(t => t.FamilyId == familyId), now, cancellationToken);

    public Task<int> RevokeDeviceAsync(Guid userId, string deviceId, DateTimeOffset now, CancellationToken cancellationToken) =>
        RevokeActiveAsync(db.RefreshTokens.Where(t => t.UserId == userId && t.DeviceId == deviceId), now, cancellationToken);

    /// <summary>Signs the user out everywhere.</summary>
    public Task<int> RevokeUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        RevokeActiveAsync(db.RefreshTokens.Where(t => t.UserId == userId), now, cancellationToken);

    private static Task<int> RevokeActiveAsync(
        IQueryable<RefreshToken> tokens, DateTimeOffset now, CancellationToken cancellationToken) =>
        tokens.Where(t => t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
}
