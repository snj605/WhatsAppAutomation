using System.ComponentModel.DataAnnotations;

namespace WhatsAppAutomation.Infrastructure.WhatsApp;

/// <summary>How the API reaches the Node.js WhatsApp gateway. Section "Gateway" in configuration.</summary>
public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>Header that carries the shared secret, both ways.</summary>
    public const string SecretHeader = "X-Gateway-Secret";

    /// <summary>The gateway's address, e.g. http://127.0.0.1:5180. It must not be reachable from the internet.</summary>
    [Required]
    public Uri BaseUrl { get; set; } = new("http://127.0.0.1:5180");

    /// <summary>
    /// Shared secret, at least 32 characters. The API sends it to the gateway, and the gateway sends
    /// it back with its events. Keep it in user secrets or an environment variable.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    public bool IsConfigured => Secret.Length >= 32;
}
