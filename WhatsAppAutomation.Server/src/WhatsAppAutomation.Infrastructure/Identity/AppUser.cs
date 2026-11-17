using Microsoft.AspNetCore.Identity;

namespace WhatsAppAutomation.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>A disabled user cannot sign in, and their sessions stop refreshing.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
