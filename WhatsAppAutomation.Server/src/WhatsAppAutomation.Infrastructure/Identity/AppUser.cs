using Microsoft.AspNetCore.Identity;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>A disabled user cannot sign in, and their sessions stop refreshing.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public UserKind Kind { get; set; } = UserKind.Member;

    /// <summary>The tenant the user belongs to. Null only for platform admins.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Set when someone else chose the password: the user must choose their own before doing anything else.</summary>
    public bool MustChangePassword { get; set; }
}
