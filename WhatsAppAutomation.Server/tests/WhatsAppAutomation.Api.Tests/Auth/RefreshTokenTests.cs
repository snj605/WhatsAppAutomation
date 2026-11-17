using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Api.Tests.Auth;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Token_is_active_until_it_expires()
    {
        var token = new RefreshToken(Guid.NewGuid(), new string('a', 64), Guid.NewGuid(), "phone-1", null, Now, Now.AddDays(1));

        Assert.True(token.IsActive(Now.AddHours(23)));
        Assert.False(token.IsActive(Now.AddDays(1)));
    }

    [Fact]
    public void Expiry_must_be_after_creation()
    {
        Assert.Throws<ArgumentException>(() =>
            new RefreshToken(Guid.NewGuid(), new string('a', 64), Guid.NewGuid(), "phone-1", null, Now, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Device_id_is_required(string deviceId)
    {
        Assert.Throws<ArgumentException>(() =>
            new RefreshToken(Guid.NewGuid(), new string('a', 64), Guid.NewGuid(), deviceId, null, Now, Now.AddDays(1)));
    }
}
