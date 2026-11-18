using System.Collections.Concurrent;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Application.Features.WhatsApp;

namespace WhatsAppAutomation.Api.Tests.Infrastructure;

/// <summary>
/// Stands in for the Node.js gateway: automated tests never talk to WhatsApp. Behaviour is chosen
/// by phone number so parallel tests do not interfere.
/// </summary>
public sealed class FakeWhatsAppGateway : IWhatsAppGateway
{
    public const string NotOnWhatsAppNumber = "+15550000000";
    public const string GatewayDownNumber = "+15550000001";

    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>Numbers whose sends fail with a general send error.</summary>
    public ConcurrentBag<string> FailNumbers { get; } = new();

    public Task<Result> LinkAsync(Guid accountId, LinkMethod method, string? phoneNumber, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"link {accountId} {method} {phoneNumber}");
        return Task.FromResult(phoneNumber == GatewayDownNumber ? WhatsAppErrors.GatewayUnavailable : Result.Success());
    }

    public Task<Result> ReconnectAsync(Guid accountId, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"reconnect {accountId}");
        return Task.FromResult(Result.Success());
    }

    /// <summary>Accounts whose unlinking reports that WhatsApp could not be reached.</summary>
    public ConcurrentBag<Guid> UnreachableOnUnlink { get; } = new();

    public Task<Result<bool>> UnlinkAsync(Guid accountId, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"unlink {accountId}");
        return Task.FromResult(Result<bool>.Success(!UnreachableOnUnlink.Contains(accountId)));
    }

    public Task<Result<string>> SendTextAsync(
        Guid accountId, string toPhoneNumber, string text, Guid reference, CancellationToken cancellationToken, GatewayFile? file = null)
    {
        Calls.Enqueue(file is null
            ? $"send {accountId} {toPhoneNumber} {reference}"
            : $"send {accountId} {toPhoneNumber} {reference} file {file.Id} {file.FileName} {file.ContentType}");
        Result<string> result = toPhoneNumber switch
        {
            NotOnWhatsAppNumber => WhatsAppErrors.NotOnWhatsApp,
            GatewayDownNumber => WhatsAppErrors.GatewayUnavailable,
            _ when FailNumbers.Contains(toPhoneNumber) => WhatsAppErrors.SendFailed,
            _ => $"WA-{reference:N}",
        };
        return Task.FromResult(result);
    }

    /// <summary>Every number is on WhatsApp except <see cref="NotOnWhatsAppNumber"/>.</summary>
    public Task<Result<IReadOnlyDictionary<string, bool>>> CheckNumbersAsync(
        Guid accountId, IReadOnlyList<string> phoneNumbers, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"check {accountId} {string.Join(',', phoneNumbers)}");
        IReadOnlyDictionary<string, bool> answer = phoneNumbers.Distinct().ToDictionary(n => n, n => n != NotOnWhatsAppNumber);
        return Task.FromResult(Result<IReadOnlyDictionary<string, bool>>.Success(answer));
    }

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>Records what would have been pushed to the apps over SignalR.</summary>
public sealed class RecordingNotifier : IWhatsAppNotifier
{
    public ConcurrentQueue<(Guid UserId, string Kind, object Payload)> Sent { get; } = new();

    public Task QrCodeAsync(Guid userId, string qr, CancellationToken cancellationToken) => Add(userId, "qr", qr);

    public Task PairingCodeAsync(Guid userId, string code, CancellationToken cancellationToken) => Add(userId, "pairingCode", code);

    public Task AccountChangedAsync(Guid userId, WhatsAppAccountDto account, CancellationToken cancellationToken) =>
        Add(userId, "account", account);

    public Task MessageChangedAsync(Guid userId, OutgoingMessageDto message, CancellationToken cancellationToken) =>
        Add(userId, "message", message);

    public Task ContactsCheckedAsync(Guid userId, WhatsAppCheckSummaryDto summary, CancellationToken cancellationToken) =>
        Add(userId, "contacts", summary);

    public Task NotificationAsync(
        Guid userId, WhatsAppAutomation.Application.Features.Notifications.NotificationDto notification, CancellationToken cancellationToken) =>
        Add(userId, "notification", notification);

    private Task Add(Guid userId, string kind, object payload)
    {
        Sent.Enqueue((userId, kind, payload));
        return Task.CompletedTask;
    }
}
