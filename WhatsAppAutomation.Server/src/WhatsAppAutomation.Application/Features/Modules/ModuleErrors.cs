using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Modules;

public static class ModuleErrors
{
    public static readonly Error NotSignedIn = new("modules.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);

    public static readonly Error NotFound = new("modules.not_found", "This occasion does not exist.", ErrorKind.NotFound);

    public static readonly Error NameTaken =
        new("modules.name_taken", "You already have an occasion with this name.", ErrorKind.Conflict);

    public static readonly Error OneBirthday =
        new("modules.one_birthday", "You already have a Birthday occasion.", ErrorKind.Conflict);


    public static readonly Error ContactNotFound =
        new("modules.contact_not_found", "This contact does not exist.", ErrorKind.NotFound);

    public static readonly Error TemplateNotFound =
        new("modules.template_not_found", "This template does not exist any more. Choose another one.", ErrorKind.Validation);

    public static readonly Error TooMany =
        new("modules.too_many", "You can have at most 20 occasions.", ErrorKind.Conflict);
}
