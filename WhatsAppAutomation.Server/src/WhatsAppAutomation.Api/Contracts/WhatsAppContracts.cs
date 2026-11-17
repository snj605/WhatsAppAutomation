namespace WhatsAppAutomation.Api.Contracts;

/// <param name="Method">"qr" to scan a QR code, or "code" to type an 8-character code on the phone.</param>
/// <param name="PhoneNumber">For "code" only: the number being linked, with country code.</param>
public sealed record LinkWhatsAppRequest(string Method, string? PhoneNumber);

public sealed record SendTestMessageRequest(string To, string Text);

/// <summary>What the gateway posts to /internal/whatsapp/events. Fields depend on Type.</summary>
public sealed record GatewayEventRequest(
    string Type,
    Guid AccountId,
    string? Qr,
    string? Code,
    string? Status,
    string? Reason,
    string? PhoneNumber,
    string? Name,
    Guid? Reference,
    DateTimeOffset? At);
