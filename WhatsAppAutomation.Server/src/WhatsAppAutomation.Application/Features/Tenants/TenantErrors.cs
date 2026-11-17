using WhatsAppAutomation.Application.Common.Models;

namespace WhatsAppAutomation.Application.Features.Tenants;

public static class TenantErrors
{
    public static readonly Error NotFound = new("tenants.not_found", "This tenant does not exist.", ErrorKind.NotFound);

    public static readonly Error NameTaken =
        new("tenants.name_taken", "Another tenant already has this name.", ErrorKind.Conflict);

    public static readonly Error StillActive = new(
        "tenants.still_active", "Turn the tenant off first. Deleting removes all of its people and their data.", ErrorKind.Conflict);
}
