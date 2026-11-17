using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.WhatsApp;

public static class WhatsAppErrors
{
    public static readonly Error AlreadyLinked =
        new("whatsapp.already_linked", "A WhatsApp number is already linked. Unlink it first.", ErrorKind.Conflict);

    public static readonly Error NotConnected =
        new("whatsapp.not_connected", "Your WhatsApp number is not connected. Link it, or wait until it reconnects.", ErrorKind.Conflict);

    public static readonly Error InvalidPhoneNumber = new(
        "whatsapp.invalid_phone",
        "Enter the number in international form with the country code, for example +91 98765 43210.",
        ErrorKind.Validation);

    public static readonly Error NotOnWhatsApp =
        new("whatsapp.not_on_whatsapp", "This number is not on WhatsApp.", ErrorKind.Validation);

    public static readonly Error GatewayUnavailable = new(
        "whatsapp.gateway_unavailable",
        "The WhatsApp service is not running or not set up. See the README, then try again.",
        ErrorKind.Unavailable);

    public static readonly Error SendFailed =
        new("whatsapp.send_failed", "WhatsApp did not accept the message. Try again later.", ErrorKind.Unavailable);

    public static readonly Error NotSignedIn =
        new("whatsapp.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);

    public static readonly Error UnknownAccount =
        new("whatsapp.unknown_account", "Unknown WhatsApp account.", ErrorKind.NotFound);
}
