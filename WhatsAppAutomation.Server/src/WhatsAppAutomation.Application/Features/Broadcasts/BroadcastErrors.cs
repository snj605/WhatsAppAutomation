using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Broadcasts;

public static class BroadcastErrors
{
    public static readonly Error NotSignedIn = new("broadcasts.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);

    public static readonly Error OnlySingleMessages = new(
        "broadcasts.only_single", "You can send to one contact at a time. Ask your admin to turn on Broadcasts.", ErrorKind.Forbidden);

    public static readonly Error NotFound = new("broadcasts.not_found", "This broadcast does not exist.", ErrorKind.NotFound);

    public static readonly Error NoText = new("broadcasts.no_text", "Write the message in at least one language.", ErrorKind.Validation);

    public static readonly Error NoRecipients =
        new("broadcasts.no_recipients", "Choose at least one category, group or contact.", ErrorKind.Validation);

    public static readonly Error NobodyToSend = new(
        "broadcasts.nobody_to_send",
        "None of the chosen contacts can get this message. Check the skipped list: names or texts may be missing.",
        ErrorKind.Validation);

    public static readonly Error NotPausable = new("broadcasts.not_sending", "Only a broadcast that is sending can be paused.", ErrorKind.Conflict);

    public static readonly Error NotPaused = new("broadcasts.not_paused", "Only a paused broadcast can be resumed.", ErrorKind.Conflict);

    public static readonly Error Finished = new("broadcasts.finished", "This broadcast has already finished.", ErrorKind.Conflict);
}
