using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Contacts;

public static class ContactErrors
{
    public static readonly Error NotSignedIn = new("contacts.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);

    public static readonly Error UnknownSalutation =
        new("contacts.unknown_salutation", "Pick a salutation from your list (Occasions → Salutations).", ErrorKind.Validation);

    public static readonly Error ContactNotFound = new("contacts.not_found", "This contact does not exist.", ErrorKind.NotFound);

    public static readonly Error CategoryNotFound = new("categories.not_found", "This category does not exist.", ErrorKind.NotFound);

    public static readonly Error GroupNotFound = new("groups.not_found", "One of the groups does not exist.", ErrorKind.NotFound);

    public static readonly Error InvalidPhoneNumber = new(
        "contacts.invalid_phone",
        "Enter the number in international form with the country code, for example +91 98765 43210.",
        ErrorKind.Validation);

    public static readonly Error InvalidBirthday = new("contacts.invalid_birthday", "This birthday is not a real date.", ErrorKind.Validation);

    public static readonly Error DuplicatePhone =
        new("contacts.duplicate_phone", "You already have a contact with this number.", ErrorKind.Conflict);

    public static readonly Error DuplicateCategory =
        new("categories.duplicate", "You already have a category with this name.", ErrorKind.Conflict);

    public static readonly Error DuplicateGroup = new("groups.duplicate", "You already have a group with this name.", ErrorKind.Conflict);

    public static readonly Error CategoryInUse = new(
        "categories.in_use", "Contacts still use this category. Move them to another category first.", ErrorKind.Conflict);

    public static Error MissingName(string languageName) => new(
        "contacts.missing_name",
        $"Enter the real name in {languageName}, the contact's language. Messages use it.",
        ErrorKind.Validation);
}
