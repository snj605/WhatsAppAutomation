using Microsoft.AspNetCore.Identity;

namespace WhatsAppAutomation.Infrastructure.Identity;

public sealed class AppRole : IdentityRole<Guid>
{
    public string? Description { get; set; }

    /// <summary>True only for Owner: it has every permission and cannot be edited or deleted.</summary>
    public bool IsSystem { get; set; }
}
