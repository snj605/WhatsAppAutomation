using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Domain.ValueObjects;

namespace WhatsAppAutomation.Application.Features.WhatsApp.Commands;

/// <summary>
/// Sends one text message from the signed-in user's linked number, right away. Meant for checking
/// that linking works; broadcasts will go through the paced sender instead.
/// </summary>
public sealed record SendTestMessageCommand(string ToPhoneNumber, string Text) : ICommand<OutgoingMessageDto>;

internal sealed class SendTestMessageCommandValidator : AbstractValidator<SendTestMessageCommand>
{
    public SendTestMessageCommandValidator()
    {
        RuleFor(c => c.ToPhoneNumber).NotEmpty().MaximumLength(30);
        RuleFor(c => c.Text).NotEmpty().MaximumLength(OutgoingMessage.TextMaxLength)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Write a message.");
    }
}

internal sealed class SendTestMessageCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IWhatsAppGateway gateway, IWhatsAppNotifier notifier, TimeProvider clock)
    : ICommandHandler<SendTestMessageCommand, OutgoingMessageDto>
{
    public async Task<Result<OutgoingMessageDto>> Handle(SendTestMessageCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return WhatsAppErrors.NotSignedIn;
        }

        if (PhoneNumber.Normalize(command.ToPhoneNumber) is not { } to)
        {
            return WhatsAppErrors.InvalidPhoneNumber;
        }

        var account = await db.WhatsAppAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
        if (account is null || !account.CanSend)
        {
            return WhatsAppErrors.NotConnected;
        }

        // Saved before sending: its id is the idempotency key, and status ticks can find it even if
        // they arrive before the gateway's answer.
        var message = new OutgoingMessage(account.Id, userId, to, command.Text, clock.GetUtcNow());
        db.OutgoingMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        // Not cancelled with the request: once handed to WhatsApp, the result must be recorded.
        var sent = await gateway.SendTextAsync(account.Id, to, command.Text, message.Id, CancellationToken.None);

        // A status tick may update the same row at the same moment; the save retries on that conflict.
        await OutgoingMessageUpdates.SaveWithRetryAsync(db, message, m =>
        {
            if (sent.IsSuccess)
            {
                m.MarkSent(sent.Value, clock.GetUtcNow());
            }
            else
            {
                m.MarkFailed(sent.Error!.Code);
            }

            return true;
        });
        var dto = OutgoingMessageDto.From(message);
        await notifier.MessageChangedAsync(userId, dto, CancellationToken.None);
        return sent.IsSuccess ? dto : sent.Error!;
    }
}
