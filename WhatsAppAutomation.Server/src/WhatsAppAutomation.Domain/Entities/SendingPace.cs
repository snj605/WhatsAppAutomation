namespace WhatsAppAutomation.Domain.Entities;

/// <summary>
/// The paced sender's memory for one WhatsApp number: when the next message may go, how many went since the
/// last rest, and failures in a row. Saved in the database so a restart of the API does not reset the rests
/// or the failure count (and so the next message still waits its turn).
/// </summary>
public sealed class SendingPace
{
    private SendingPace()
    {
    }

    public SendingPace(Guid accountId, DateTimeOffset nextSendAt)
    {
        AccountId = accountId;
        NextSendAt = nextSendAt;
    }

    public Guid AccountId { get; private set; }

    public DateTimeOffset NextSendAt { get; private set; }

    public int SentSinceRest { get; private set; }

    public int FailuresInRow { get; private set; }

    /// <summary>Nothing was sent (WhatsApp not reachable): try again at this time.</summary>
    public void Postpone(DateTimeOffset until) => NextSendAt = until;

    public void RecordSuccess() => FailuresInRow = 0;

    public void RecordFailure() => FailuresInRow++;

    public void ResetFailures() => FailuresInRow = 0;

    /// <summary>Counts one handed-over message; returns how many went since the last rest, this one included.</summary>
    public int CountSent() => ++SentSinceRest;

    /// <summary>The wait after this message is a rest: the count starts again.</summary>
    public void Rested() => SentSinceRest = 0;

    public void ScheduleNext(DateTimeOffset at) => NextSendAt = at;
}
