using WhatsAppAutomation.Application.Features.Broadcasts;

namespace WhatsAppAutomation.Application.Tests.Features.Broadcasts;

public sealed class BroadcastRenderTests
{
    [Theory]
    [InlineData("Hello {{name}}", "Hello Rameshbhai")]
    [InlineData("Hello {{ name }}", "Hello Rameshbhai")]
    [InlineData("Hello {name}", "Hello Rameshbhai")]
    [InlineData("Hello {NAME}", "Hello Rameshbhai")]
    public void Both_placeholder_styles_get_the_name(string text, string expected) =>
        Assert.Equal(expected, BroadcastPlanner.Render(text, "Rameshbhai", null, new Random(1)));

    [Fact]
    public void An_empty_salutation_leaves_no_double_space() =>
        Assert.Equal("Rameshbhai, happy Diwali", BroadcastPlanner.Render("{{salutation}} {{name}}, happy Diwali", "Rameshbhai", null, new Random(1)));

    [Fact]
    public void A_word_choice_becomes_one_of_its_options()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var text = BroadcastPlanner.Render("{Hi|Hello|Namaste} {{name}}", "Asha", null, new Random(seed));
            Assert.Contains(text, new[] { "Hi Asha", "Hello Asha", "Namaste Asha" });
        }
    }

    [Fact]
    public void Different_people_can_get_different_options()
    {
        var results = Enumerable.Range(0, 50).Select(seed => BroadcastPlanner.Render("{Hi|Hello|Namaste}", null, null, new Random(seed))).ToHashSet();
        Assert.True(results.Count > 1);
    }

    [Theory]
    [InlineData("{city} stays")]
    [InlineData("no braces")]
    public void Text_without_options_or_placeholders_is_unchanged(string text) =>
        Assert.Equal(text, BroadcastPlanner.Render(text, "X", null, new Random(1)));

    [Theory]
    [InlineData("Hi {{name}}", true)]
    [InlineData("Hi {name}", true)]
    [InlineData("Hi {Hi|Hello}", false)]
    [InlineData("Hi {{salutation}}", false)]
    public void UsesName_sees_both_styles(string text, bool expected) => Assert.Equal(expected, BroadcastPlanner.UsesName(text));
}
