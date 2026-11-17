using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Authentication;

namespace WhatsAppAutomation.Infrastructure.Identity;

internal sealed class JwtAccessTokenIssuer(IOptions<AuthOptions> options, TimeProvider clock) : IAccessTokenIssuer
{
    public const string DeviceIdClaim = "did";
    public const string DisplayNameClaim = "display_name";

    private readonly JsonWebTokenHandler _handler = new();

    public IssuedAccessToken Issue(UserSummary user, string deviceId)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.Add(settings.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, user.UserName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(DisplayNameClaim, user.DisplayName),
            new(DeviceIdClaim, deviceId),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(CreateKey(settings.SigningKey), SecurityAlgorithms.HmacSha256),
        };

        return new IssuedAccessToken(_handler.CreateToken(descriptor), expires);
    }

    public static SymmetricSecurityKey CreateKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));
}
