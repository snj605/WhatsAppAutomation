using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.WhatsApp;

public sealed record WhatsAppAccountDto(
    WhatsAppConnectionStatus Status,
    string? StatusReason,
    DateTimeOffset? StatusChangedAt,
    string? PhoneNumber,
    string? ProfileName,
    DateTimeOffset? LinkedAt)
{
    public static readonly WhatsAppAccountDto NotLinked = new(WhatsAppConnectionStatus.NotLinked, null, null, null, null, null);

    public static WhatsAppAccountDto From(WhatsAppAccount? account) => account is null
        ? NotLinked
        : new(account.Status, account.StatusReason, account.StatusChangedAt, account.PhoneNumber, account.ProfileName, account.LinkedAt);
}

public sealed record OutgoingMessageDto(
    Guid Id,
    string ToPhoneNumber,
    string Text,
    OutgoingMessageStatus Status,
    string? ErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt)
{
    public static OutgoingMessageDto From(OutgoingMessage m) =>
        new(m.Id, m.ToPhoneNumber, m.Text, m.Status, m.ErrorCode, m.CreatedAt, m.SentAt, m.DeliveredAt, m.ReadAt);
}
