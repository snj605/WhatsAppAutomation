using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Tests.Infrastructure;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Tests.Auth;

public sealed class AuthApiTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_with_correct_password_returns_a_session_for_that_user()
    {
        var userName = await factory.CreateUserAsync(UserKind.TenantAdmin);

        var session = await LoginAsync(userName, "phone-1");

        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(session.RefreshToken));
        Assert.Equal(userName, session.User.UserName);
        Assert.Equal(UserKinds.TenantAdminCode, session.User.Kind);
        Assert.False(session.User.MustChangePassword);
        Assert.NotNull(session.User.TenantName);
        Assert.True(session.RefreshTokenExpiresAt > session.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task Refresh_token_is_stored_only_as_a_hash()
    {
        var userName = await factory.CreateUserAsync();
        var session = await LoginAsync(userName, "phone-1");

        await factory.WithDbAsync(async db =>
        {
            var stored = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == session.User.Id).ToListAsync(Ct);
            var token = Assert.Single(stored);
            Assert.NotEqual(session.RefreshToken, token.TokenHash);
            Assert.Equal(64, token.TokenHash.Length);
        });
    }

    [Theory]
    [InlineData("wrong password here")]
    [InlineData("")]
    public async Task Login_with_wrong_or_empty_password_is_rejected(string password)
    {
        var userName = await factory.CreateUserAsync();

        var response = await PostLoginAsync(userName, password, "phone-1");

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_give_the_same_answer()
    {
        var userName = await factory.CreateUserAsync();

        var wrongPassword = await PostLoginAsync(userName, "wrong password here", "phone-1");
        var unknownUser = await PostLoginAsync("nobody_" + Guid.NewGuid().ToString("N")[..8], "wrong password here", "phone-1");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(await ProblemCodeAsync(wrongPassword), await ProblemCodeAsync(unknownUser));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var userName = await factory.CreateUserAsync();
        for (var i = 0; i < 5; i++)
        {
            await PostLoginAsync(userName, "wrong password here", "phone-1");
        }

        var response = await PostLoginAsync(userName, ApiFactory.TestPassword, "phone-1");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("auth.locked_out", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Disabled_user_cannot_sign_in()
    {
        var userName = await factory.CreateUserAsync(isActive: false);

        var response = await PostLoginAsync(userName, ApiFactory.TestPassword, "phone-1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user_and_requires_a_token()
    {
        var userName = await factory.CreateUserAsync();
        var session = await LoginAsync(userName, "phone-1");

        var anonymous = await _client.GetAsync("/api/v1/auth/me", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var response = await _client.SendAsync(request, Ct);

        response.EnsureSuccessStatusCode();
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(Ct);
        Assert.Equal(userName, me!.User.UserName);
    }

    [Fact]
    public async Task Me_rejects_a_tampered_token()
    {
        var userName = await factory.CreateUserAsync();
        var session = await LoginAsync(userName, "phone-1");
        var tampered = session.AccessToken[..^4] + (session.AccessToken.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tampered);
        var response = await _client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_stops_working()
    {
        var userName = await factory.CreateUserAsync();
        var first = await LoginAsync(userName, "phone-1");

        var second = await RefreshAsync(first.RefreshToken, "phone-1");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var rotated = (await second.Content.ReadFromJsonAsync<SessionResponse>(Ct))!;
        Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);

        var reuse = await RefreshAsync(first.RefreshToken, "phone-1");
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
    }

    [Fact]
    public async Task Reusing_an_old_refresh_token_ends_the_whole_session()
    {
        var userName = await factory.CreateUserAsync();
        var first = await LoginAsync(userName, "phone-1");
        var rotated = (await (await RefreshAsync(first.RefreshToken, "phone-1")).Content.ReadFromJsonAsync<SessionResponse>(Ct))!;

        await RefreshAsync(first.RefreshToken, "phone-1"); // the stolen copy is replayed

        var legitimate = await RefreshAsync(rotated.RefreshToken, "phone-1");
        Assert.Equal(HttpStatusCode.Unauthorized, legitimate.StatusCode);
    }

    [Fact]
    public async Task Refresh_from_another_device_is_rejected_and_ends_the_session()
    {
        var userName = await factory.CreateUserAsync();
        var session = await LoginAsync(userName, "phone-1");

        var otherDevice = await RefreshAsync(session.RefreshToken, "attacker-device");
        var ownDevice = await RefreshAsync(session.RefreshToken, "phone-1");

        Assert.Equal(HttpStatusCode.Unauthorized, otherDevice.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ownDevice.StatusCode);
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected()
    {
        var userName = await factory.CreateUserAsync();
        var session = await LoginAsync(userName, "phone-1");
        await factory.WithDbAsync(db => db.RefreshTokens
            .Where(t => t.UserId == session.User.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));

        var response = await RefreshAsync(session.RefreshToken, "phone-1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_refreshes_with_one_token_never_both_succeed()
    {
        var userName = await factory.CreateUserAsync();
        var session = await LoginAsync(userName, "phone-1");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshAsync(session.RefreshToken, "phone-1")));

        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) <= 1);
    }

    [Fact]
    public async Task Each_device_has_its_own_session_and_logout_ends_only_that_one()
    {
        var userName = await factory.CreateUserAsync();
        var phone = await LoginAsync(userName, "phone-1");
        var web = await LoginAsync(userName, "web-1");

        var logout = await _client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest(phone.RefreshToken), Ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(phone.RefreshToken, "phone-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(web.RefreshToken, "web-1")).StatusCode);
    }

    [Fact]
    public async Task Signing_in_again_on_the_same_device_ends_the_previous_session_there()
    {
        var userName = await factory.CreateUserAsync();
        var first = await LoginAsync(userName, "phone-1");
        await LoginAsync(userName, "phone-1");

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first.RefreshToken, "phone-1")).StatusCode);
    }

    [Fact]
    public async Task Logout_with_an_unknown_token_succeeds_without_revealing_anything()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest("not-a-real-token"), Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy_when_the_database_is_reachable()
    {
        var response = await _client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Confirming_the_password_before_a_change_checks_it_without_ending_the_session()
    {
        var userName = await factory.CreateUserAsync();
        var session = await LoginAsync(userName, "phone-1");

        async Task<HttpResponseMessage> ConfirmAsync(string password, string? token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/confirm-password")
            {
                Content = JsonContent.Create(new ConfirmPasswordRequest(password)),
            };
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return await _client.SendAsync(request, Ct);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(ApiFactory.TestPassword, session.AccessToken)).StatusCode);
        // A wrong password is a plain mistake (400), not a signed-out session (401).
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync("wrong password here", session.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ConfirmAsync(ApiFactory.TestPassword, null)).StatusCode);
    }

    private async Task<SessionResponse> LoginAsync(string userName, string deviceId)
    {
        var response = await PostLoginAsync(userName, ApiFactory.TestPassword, deviceId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionResponse>(Ct))!;
    }

    private Task<HttpResponseMessage> PostLoginAsync(string userName, string password, string deviceId) =>
        _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, password, deviceId, "Test device"), Ct);

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken, string deviceId) =>
        _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(refreshToken, deviceId), Ct);

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(Ct);
        return problem?.GetValueOrDefault("code")?.ToString();
    }
}
