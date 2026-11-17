namespace WhatsAppAutomation.Application.Common.Interfaces;

/// <summary>
/// Makes sure only one sender works on a WhatsApp number at a time, also when several copies of the API run.
/// Without it two senders could pick the same person (two messages), or send two messages at the same moment
/// and break the pacing and the daily limit.
/// </summary>
public interface ISendTurn
{
    /// <summary>
    /// Takes the turn for this number, or returns null when another sender has it. Dispose the result to give
    /// the turn back. A sender that crashes loses its turn by itself.
    /// </summary>
    Task<IAsyncDisposable?> TryTakeAsync(Guid accountId, CancellationToken cancellationToken);
}
