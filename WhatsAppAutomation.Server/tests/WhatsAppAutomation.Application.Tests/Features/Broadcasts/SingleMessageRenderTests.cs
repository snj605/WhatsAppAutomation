using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Tests.Features.Broadcasts;

public sealed class SingleMessageRenderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private static readonly Salutation Pujya = new(Guid.NewGuid(), "Pujya", "પૂજ્ય", 1, T0);

    private static readonly Dictionary<Guid, Salutation> Salutations = new() { [Pujya.Id] = Pujya };

    private static Contact Person(
        string? saved = "Daddy", string? en = "Rameshbhai", string? hi = null, string? gu = "રમેશભાઈ", bool salutation = true,
        ContactLanguage language = ContactLanguage.Gu)
    {
        var c = new Contact(Guid.NewGuid(), "+919876543210", T0);
        c.Update(new ContactDetails("+919876543210", saved, en, hi, gu, language, null, salutation ? Pujya : null, null, null), T0);
        return c;
    }

    [Fact]
    public void Language_names_and_salutation_are_filled_in()
    {
        var (text, missing) = BroadcastPlanner.RenderFor("{{salutation}} {{name_gu}}, happy Diwali", Person(), new Random(1), salutations: Salutations);
        Assert.Null(missing);
        Assert.Equal("પૂજ્ય રમેશભાઈ, happy Diwali", text);
    }

    [Fact]
    public void The_salutation_is_in_the_contacts_language()
    {
        var english = Person(language: ContactLanguage.En);
        Assert.Equal("Pujya Rameshbhai", BroadcastPlanner.RenderFor("{{salutation}} {{name}}", english, new Random(1), salutations: Salutations).Text);
        Assert.Equal("Pujya", english.Salutation); // the English text is kept beside the link
    }

    [Fact]
    public void Plain_name_is_the_English_name_else_the_saved_name()
    {
        Assert.Equal("Hi Rameshbhai", BroadcastPlanner.RenderFor("Hi {{name}}", Person(), new Random(1)).Text);
        Assert.Equal("Hi Daddy", BroadcastPlanner.RenderFor("Hi {name}", Person(en: null), new Random(1)).Text);
    }

    [Fact]
    public void A_missing_language_name_skips_the_person_instead_of_sending_a_blank()
    {
        var (text, missing) = BroadcastPlanner.RenderFor("नमस्ते {{name_hi}}", Person(), new Random(1));
        Assert.Null(text);
        Assert.Equal("Hindi name", missing);
    }

    [Fact]
    public void Single_brace_language_names_work_too() =>
        Assert.Equal("Jai Shri Krishna રમેશભાઈ", BroadcastPlanner.RenderFor("Jai Shri Krishna {name_gu}", Person(), new Random(1)).Text);

    [Fact]
    public void An_empty_salutation_leaves_no_double_space() =>
        Assert.Equal("Rameshbhai, hello", BroadcastPlanner.RenderFor("{{salutation}} {{name_en}}, hello", Person(salutation: false), new Random(1)).Text);
}
