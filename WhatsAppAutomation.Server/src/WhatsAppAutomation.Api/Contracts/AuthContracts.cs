namespace WhatsAppAutomation.Api.Contracts;

/// <param name="DeviceId">A random id the app creates once per install and keeps in secure storage.</param>
/// <param name="DeviceName">Optional label shown in the list of signed-in devices, e.g. "Pixel 8".</param>
public sealed record LoginRequest(string UserName, string Password, string DeviceId, string? DeviceName);

public sealed record RefreshRequest(string RefreshToken, string DeviceId);

public sealed record LogoutRequest(string RefreshToken);

public sealed record UserResponse(Guid Id, string UserName, string DisplayName, IReadOnlyList<string> Roles);

public sealed record SessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserResponse User);

public sealed record MeResponse(UserResponse User, IReadOnlyList<string> Permissions);

/// <summary>Sign-in from the browser app. The device id is kept by the server in a cookie.</summary>
public sealed record WebLoginRequest(string UserName, string Password);

/// <summary>What the browser receives: no refresh token, which stays in an HttpOnly cookie.</summary>
public sealed record WebSessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, UserResponse User);
