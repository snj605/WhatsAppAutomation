using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace WhatsAppAutomation.Application.Features.Authentication;

/// <summary>Creates refresh token secrets and the hash that is stored instead of them.</summary>
internal static class RefreshTokenSecret
{
    private const int SecretBytes = 32;

    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));

    public static string Hash(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
