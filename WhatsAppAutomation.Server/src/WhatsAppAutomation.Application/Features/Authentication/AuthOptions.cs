using System.ComponentModel.DataAnnotations;

namespace WhatsAppAutomation.Application.Features.Authentication;

/// <summary>Bound from the "Auth" configuration section.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>JWT issuer and audience. Any stable string; the defaults are fine for one server.</summary>
    [Required]
    public string Issuer { get; set; } = "whatsappautomation";

    [Required]
    public string Audience { get; set; } = "whatsappautomation-clients";

    /// <summary>
    /// HMAC-SHA256 signing key, at least 32 bytes once UTF-8 encoded. Never commit it: use
    /// user-secrets in development and an environment variable (Auth__SigningKey) on the server.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    [Range(typeof(TimeSpan), "01:00:00", "180.00:00:00")]
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
}
