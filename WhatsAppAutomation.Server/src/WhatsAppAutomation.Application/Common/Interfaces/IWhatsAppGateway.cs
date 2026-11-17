using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Common.Interfaces;

/// <summary>
/// A file to send with a message (the text is its caption). The gateway fetches the bytes once from
/// /internal/files/{Id}, uploads them to WhatsApp once, and reuses that upload for every recipient.
/// </summary>
public sealed record GatewayFile(Guid Id, string FileName, string ContentType);

public enum LinkMethod
{
    /// <summary>Show a QR code; scan it with WhatsApp on the phone (from another screen).</summary>
    Qr,

    /// <summary>Show an 8-character code; type it in WhatsApp on the phone. Works on the same phone.</summary>
    PhoneCode,
}

/// <summary>
/// The Node.js gateway that holds the WhatsApp connections. It is the only part of the system
/// that talks to WhatsApp. Failures come back as Result errors (see WhatsAppErrors).
/// </summary>
public interface IWhatsAppGateway
{
    /// <summary>Starts linking. QR codes or the phone code arrive later as gateway events.</summary>
    Task<Result> LinkAsync(Guid accountId, LinkMethod method, string? phoneNumber, CancellationToken cancellationToken);

    /// <summary>Logs out on WhatsApp and deletes the saved login.</summary>
    Task<Result> UnlinkAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a text and returns WhatsApp's message id. "reference" is our message id: sending the
    /// same reference again does not send twice.
    /// </summary>
    Task<Result<string>> SendTextAsync(
        Guid accountId, string toPhoneNumber, string text, Guid reference, CancellationToken cancellationToken, GatewayFile? file = null);

    /// <summary>
    /// Asks WhatsApp which of these numbers (E.164, at most 50) have WhatsApp. The answer has every number asked
    /// about, true when it is on WhatsApp.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, bool>>> CheckNumbersAsync(
        Guid accountId, IReadOnlyList<string> phoneNumbers, CancellationToken cancellationToken);

    /// <summary>True when the gateway is configured and answers.</summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}
