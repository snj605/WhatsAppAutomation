using WhatsAppAutomation.Application.Features.Members;

namespace WhatsAppAutomation.Application.Tests.Features.Members;

public sealed class PasswordRulesTests
{
    [Theory]
    [InlineData("riya42")]
    [InlineData("diwali")]
    [InlineData("om1978")]
    public void A_user_may_choose_a_short_simple_password(string password) =>
        Assert.Null(PasswordRules.Problem(password, "demo1", isAdmin: false));

    [Theory]
    [InlineData("riya4")]      // five characters
    [InlineData("123456")]     // run up
    [InlineData("654321")]     // run down
    [InlineData("abcdef")]
    [InlineData("111111")]     // one repeated character
    [InlineData("Password")]
    [InlineData("RAMESH1")]    // the user name, in another case
    public void Too_short_or_obvious_passwords_are_refused(string password) =>
        Assert.NotNull(PasswordRules.Problem(password, "ramesh1", isAdmin: false));

    [Fact]
    public void An_admin_account_needs_twelve_characters()
    {
        Assert.NotNull(PasswordRules.Problem("riya4242", "owner", isAdmin: true));
        Assert.Null(PasswordRules.Problem("riya4242riya", "owner", isAdmin: true));
    }
}
