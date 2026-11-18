using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Application.Features.WhatsApp;

namespace WhatsAppAutomation.Api.Realtime;

/// <summary>
/// Live WhatsApp updates for the signed-in user, over SignalR (/hubs/whatsapp). The server only
/// pushes; apps listen for: "qr" { qr }, "pairingCode" { code }, "account" (status), "message" (ticks),
/// "contacts" (WhatsApp check summary, after numbers were checked).
/// Each user receives only their own updates.
/// </summary>
[Authorize]
public sealed class WhatsAppHub : Hub
{
    public const string Path = "/hubs/whatsapp";
}

/// <summary>SignalR addresses users by the "sub" claim of their access token (the user id).</summary>
internal sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirst("sub")?.Value;
}

internal sealed class SignalRWhatsAppNotifier(IHubContext<WhatsAppHub> hub) : IWhatsAppNotifier
{
    public Task QrCodeAsync(Guid userId, string qr, CancellationToken cancellationToken) =>
        User(userId).SendAsync("qr", new { qr }, cancellationToken);

    public Task PairingCodeAsync(Guid userId, string code, CancellationToken cancellationToken) =>
        User(userId).SendAsync("pairingCode", new { code }, cancellationToken);

    public Task AccountChangedAsync(Guid userId, WhatsAppAccountDto account, CancellationToken cancellationToken) =>
        User(userId).SendAsync("account", account, cancellationToken);

    public Task MessageChangedAsync(Guid userId, OutgoingMessageDto message, CancellationToken cancellationToken) =>
        User(userId).SendAsync("message", message, cancellationToken);

    public Task ContactsCheckedAsync(Guid userId, WhatsAppCheckSummaryDto summary, CancellationToken cancellationToken) =>
        User(userId).SendAsync("contacts", summary, cancellationToken);

    public Task NotificationAsync(
        Guid userId, Application.Features.Notifications.NotificationDto notification, CancellationToken cancellationToken) =>
        User(userId).SendAsync("notification", notification, cancellationToken);

    private IClientProxy User(Guid userId) => hub.Clients.User(userId.ToString());
}
