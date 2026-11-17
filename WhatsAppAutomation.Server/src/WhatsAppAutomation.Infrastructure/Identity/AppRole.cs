using Microsoft.AspNetCore.Identity;

namespace WhatsAppAutomation.Infrastructure.Identity;

/// <summary>
/// Required by ASP.NET Core Identity. Access no longer comes from roles but from the kind of user and the
/// member's switches (see UserKinds); the table is kept so older databases migrate without losing anything.
/// </summary>
public sealed class AppRole : IdentityRole<Guid>
{
    public string? Description { get; set; }

    public bool IsSystem { get; set; }
}
