using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Application.Features.WhatsApp;

namespace WhatsAppAutomation.Application.Common.Interfaces;

/// <summary>Pushes live WhatsApp updates to the signed-in apps of one user (SignalR in the API).</summary>
public interface IWhatsAppNotifier
{
    /// <param name="image">The same QR drawn as a PNG data URL, for apps that do not draw it themselves.</param>
    Task QrCodeAsync(Guid userId, string qr, string? image, CancellationToken cancellationToken);

    Task PairingCodeAsync(Guid userId, string code, CancellationToken cancellationToken);

    Task AccountChangedAsync(Guid userId, WhatsAppAccountDto account, CancellationToken cancellationToken);

    Task MessageChangedAsync(Guid userId, OutgoingMessageDto message, CancellationToken cancellationToken);

    /// <summary>Some contacts got a WhatsApp check result: apps refresh their contact lists.</summary>
    Task ContactsCheckedAsync(Guid userId, WhatsAppCheckSummaryDto summary, CancellationToken cancellationToken);

    /// <summary>A new notification for the user: apps show it and update the unread count.</summary>
    Task NotificationAsync(Guid userId, Features.Notifications.NotificationDto notification, CancellationToken cancellationToken);
}
