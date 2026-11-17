using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.WhatsApp.Commands;

/// <summary>
/// Something the gateway reports: a new QR code, a pairing code, a connection change, or a message
/// tick. QR and pairing codes are passed straight to the user's app and never stored or logged.
/// </summary>
public sealed record HandleGatewayEventCommand(
    string Type,
    Guid AccountId,
    string? Qr = null,
    string? Code = null,
    string? Status = null,
    string? Reason = null,
    string? PhoneNumber = null,
    string? Name = null,
    Guid? Reference = null,
    DateTimeOffset? At = null) : ICommand;

internal sealed class HandleGatewayEventCommandHandler(
    IAppDbContext db, IWhatsAppNotifier notifier, IWhatsAppGateway gateway, TimeProvider clock) : ICommandHandler<HandleGatewayEventCommand>
{
    private static readonly Error UnknownEvent = Error.Validation("Unknown or incomplete gateway event.");

    public async Task<Result> Handle(HandleGatewayEventCommand e, CancellationToken cancellationToken)
    {
        var account = await db.WhatsAppAccounts.SingleOrDefaultAsync(a => a.Id == e.AccountId, cancellationToken);
        if (account is null)
        {
            return WhatsAppErrors.UnknownAccount;
        }

        switch (e.Type)
        {
            case "qr" when !string.IsNullOrEmpty(e.Qr) && e.Qr.Length <= 1000:
                await notifier.QrCodeAsync(account.UserId, e.Qr, cancellationToken);
                return Result.Success();

            case "pairing_code" when !string.IsNullOrEmpty(e.Code) && e.Code.Length <= 20:
                await notifier.PairingCodeAsync(account.UserId, e.Code, cancellationToken);
                return Result.Success();

            case "connection" when TryParseStatus(e.Status, out var status):
                var phone = WhatsAppAutomation.Domain.ValueObjects.PhoneNumber.Normalize(e.PhoneNumber);
                if (status == WhatsAppConnectionStatus.Connected && phone is not null &&
                    await db.WhatsAppAccounts.AnyAsync(a => a.Id != account.Id && a.PhoneNumber == phone, cancellationToken))
                {
                    // One WhatsApp number belongs to one user on the whole platform: log this link out again.
                    await gateway.UnlinkAsync(account.Id, cancellationToken);
                    account.MarkNumberInUse(clock.GetUtcNow());
                }
                else
                {
                    account.ApplyGatewayStatus(status, e.Reason, phone, e.Name, clock.GetUtcNow());
                }

                await db.SaveChangesAsync(cancellationToken);
                await notifier.AccountChangedAsync(account.UserId, WhatsAppAccountDto.From(account), cancellationToken);
                return Result.Success();

            case "message_status" when e.Reference is { } reference && TryParseMessageStatus(e.Status, out var tick):
                var message = await db.OutgoingMessages
                    .SingleOrDefaultAsync(m => m.Id == reference && m.AccountId == account.Id, cancellationToken);
                if (message is null)
                {
                    return Result.Success(); // not one of ours (or already deleted): nothing to do
                }

                var at = e.At ?? clock.GetUtcNow();
                if (await OutgoingMessageUpdates.SaveWithRetryAsync(db, message, m => m.Advance(tick, at)))
                {
                    await notifier.MessageChangedAsync(account.UserId, OutgoingMessageDto.From(message), cancellationToken);
                }

                return Result.Success();

            default:
                return UnknownEvent;
        }
    }

    private static bool TryParseStatus(string? value, out WhatsAppConnectionStatus status)
    {
        status = value switch
        {
            "not_linked" => WhatsAppConnectionStatus.NotLinked,
            "linking" => WhatsAppConnectionStatus.Linking,
            "connecting" => WhatsAppConnectionStatus.Connecting,
            "connected" => WhatsAppConnectionStatus.Connected,
            "reconnecting" => WhatsAppConnectionStatus.Reconnecting,
            "logged_out" => WhatsAppConnectionStatus.LoggedOut,
            "restricted" => WhatsAppConnectionStatus.Restricted,
            "disconnected" => WhatsAppConnectionStatus.Disconnected,
            _ => (WhatsAppConnectionStatus)(-1),
        };
        return Enum.IsDefined(status);
    }

    private static bool TryParseMessageStatus(string? value, out OutgoingMessageStatus status)
    {
        status = value switch
        {
            "sent" => OutgoingMessageStatus.Sent,
            "delivered" => OutgoingMessageStatus.Delivered,
            "read" => OutgoingMessageStatus.Read,
            "failed" => OutgoingMessageStatus.Failed,
            _ => (OutgoingMessageStatus)(-1),
        };
        return Enum.IsDefined(status);
    }
}
