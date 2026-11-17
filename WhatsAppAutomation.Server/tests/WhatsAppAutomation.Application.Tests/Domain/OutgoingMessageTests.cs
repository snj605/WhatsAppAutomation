using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Tests.Domain;

public sealed class OutgoingMessageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static OutgoingMessage NewMessage() => new(Guid.NewGuid(), Guid.NewGuid(), "+919876543210", "Hi", T0);

    [Fact]
    public void Ticks_move_forward_and_fill_in_skipped_times()
    {
        var message = NewMessage();
        message.MarkSent("WA1", T0.AddSeconds(1));

        Assert.True(message.Advance(OutgoingMessageStatus.Read, T0.AddSeconds(5)));

        Assert.Equal(OutgoingMessageStatus.Read, message.Status);
        Assert.Equal(T0.AddSeconds(5), message.DeliveredAt);
        Assert.Equal(T0.AddSeconds(5), message.ReadAt);
    }

    [Fact]
    public void A_late_delivered_after_read_changes_nothing()
    {
        var message = NewMessage();
        message.Advance(OutgoingMessageStatus.Read, T0.AddSeconds(5));

        Assert.False(message.Advance(OutgoingMessageStatus.Delivered, T0.AddSeconds(6)));
        Assert.Equal(OutgoingMessageStatus.Read, message.Status);
    }

    [Fact]
    public void A_tick_that_arrives_before_the_send_answer_is_kept()
    {
        var message = NewMessage();
        message.Advance(OutgoingMessageStatus.Delivered, T0.AddSeconds(2));

        message.MarkSent("WA1", T0.AddSeconds(3));

        Assert.Equal(OutgoingMessageStatus.Delivered, message.Status);
        Assert.Equal("WA1", message.WhatsAppMessageId);
    }

    [Fact]
    public void A_delivered_message_is_never_marked_failed()
    {
        var message = NewMessage();
        message.Advance(OutgoingMessageStatus.Delivered, T0);

        message.MarkFailed("late_error");

        Assert.Equal(OutgoingMessageStatus.Delivered, message.Status);
        Assert.Null(message.ErrorCode);
    }

    [Fact]
    public void Text_is_required_and_limited()
    {
        Assert.Throws<ArgumentException>(() => new OutgoingMessage(Guid.NewGuid(), Guid.NewGuid(), "+919876543210", " ", T0));
        Assert.Throws<ArgumentException>(() =>
            new OutgoingMessage(Guid.NewGuid(), Guid.NewGuid(), "+919876543210", new string('x', OutgoingMessage.TextMaxLength + 1), T0));
    }
}
