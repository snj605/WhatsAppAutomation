using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts;

/// <summary>Converts between the language enum and the short codes the API and files use.</summary>
public static class ContactLanguages
{
    public static string ToCode(ContactLanguage language) => language switch
    {
        ContactLanguage.Hi => "hi",
        ContactLanguage.Gu => "gu",
        _ => "en",
    };

    public static string DisplayName(ContactLanguage language) => language switch
    {
        ContactLanguage.Hi => "Hindi",
        ContactLanguage.Gu => "Gujarati",
        _ => "English",
    };

    /// <summary>Accepts "en", "English", "hi", "Hindi", "gu", "Gujarati" in any case.</summary>
    public static ContactLanguage? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "en" or "english" => ContactLanguage.En,
        "hi" or "hindi" => ContactLanguage.Hi,
        "gu" or "gujarati" => ContactLanguage.Gu,
        _ => null,
    };
}
