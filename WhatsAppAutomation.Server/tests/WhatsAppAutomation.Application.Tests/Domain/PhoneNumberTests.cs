using WhatsAppAutomation.Domain.ValueObjects;

namespace WhatsAppAutomation.Application.Tests.Domain;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+919876543210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData(" +91-98765-43210 ", "+919876543210")]
    [InlineData("+1 (555) 010.9999", "+15550109999")]
    [InlineData("00919876543210", "+919876543210")]
    public void Typed_numbers_with_a_country_code_are_normalized(string input, string expected) =>
        Assert.Equal(expected, PhoneNumber.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9876543210")]          // no country code: there is no default country yet
    [InlineData("+0919876543210")]      // country codes never start with 0
    [InlineData("+91 98765 4321a")]
    [InlineData("+9198+76543210")]
    [InlineData("+1234567")]            // too short
    [InlineData("+1234567890123456")]   // more than 15 digits
    public void Anything_else_is_rejected(string? input) => Assert.Null(PhoneNumber.Normalize(input));
}
