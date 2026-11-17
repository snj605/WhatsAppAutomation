using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Tests.Domain;

public sealed class WhatsAppAccountTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Linking_that_was_never_finished_times_out_after_the_window()
    {
        var account = new WhatsAppAccount(Guid.NewGuid(), T0);
        account.StartLinking(T0);

        Assert.False(account.ExpireStaleLinking(T0.AddMinutes(2)));
        Assert.Equal(WhatsAppConnectionStatus.Linking, account.Status);

        Assert.True(account.ExpireStaleLinking(T0 + WhatsAppAccount.LinkingWindow + TimeSpan.FromSeconds(1)));
        Assert.Equal(WhatsAppConnectionStatus.NotLinked, account.Status);
        Assert.Equal("link_timeout", account.StatusReason);
    }

    [Fact]
    public void Other_statuses_are_left_alone()
    {
        var account = new WhatsAppAccount(Guid.NewGuid(), T0);
        account.ApplyGatewayStatus(WhatsAppConnectionStatus.Connected, "linked", "+919876543210", "Me", T0);

        Assert.False(account.ExpireStaleLinking(T0.AddHours(5)));
        Assert.Equal(WhatsAppConnectionStatus.Connected, account.Status);
    }
}
