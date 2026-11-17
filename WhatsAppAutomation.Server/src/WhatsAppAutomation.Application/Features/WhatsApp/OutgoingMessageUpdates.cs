using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.WhatsApp;

internal static class OutgoingMessageUpdates
{
    private const int MaxAttempts = 3;

    /// <summary>
    /// Applies a change and saves it. The send request and WhatsApp's status ticks can update the
    /// same message at the same moment. The row has a version (PostgreSQL's xmin), so the second
    /// save fails instead of silently overwriting the first; then the row is read again and the
    /// change re-applied. Returns false when the change did nothing.
    /// </summary>
    public static async Task<bool> SaveWithRetryAsync(IAppDbContext db, OutgoingMessage message, Func<OutgoingMessage, bool> change)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (!change(message))
            {
                return false;
            }

            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
                return true;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                await db.Entry(message).ReloadAsync(CancellationToken.None);
            }
        }
    }
}
