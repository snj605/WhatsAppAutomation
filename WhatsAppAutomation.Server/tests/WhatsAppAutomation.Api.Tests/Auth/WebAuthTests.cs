using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Tests.Infrastructure;

namespace WhatsAppAutomation.Api.Tests.Auth;

/// <summary>
/// The browser flow: refresh token only in an HttpOnly cookie, every call protected by the XSRF
/// double-submit check. Cookies are handled by hand so each test sees exactly what is sent.
/// </summary>
public sealed class WebAuthTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sign_in_sets_an_HttpOnly_strict_refresh_cookie_and_returns_no_refresh_token()
    {
        var browser = await BrowserAsync();
        var userName = await factory.CreateUserAsync();

        var response = await browser.PostAsync("login", new WebLoginRequest(userName, ApiFactory.TestPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("waa_refresh=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth/web", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_access_token_from_web_sign_in_works_for_the_api()
    {
        var browser = await BrowserAsync();
        var session = await browser.SignInAsync(await factory.CreateUserAsync());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var me = await browser.Client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Refresh_with_the_cookie_returns_a_new_access_token_and_rotates_the_cookie()
    {
        var browser = await BrowserAsync();
        await browser.SignInAsync(await factory.CreateUserAsync());
        var firstCookie = browser.Cookies["waa_refresh"];

        var response = await browser.PostAsync("refresh");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(firstCookie, browser.Cookies["waa_refresh"]);
        var session = await response.Content.ReadFromJsonAsync<WebSessionResponse>(Ct);
        Assert.False(string.IsNullOrEmpty(session!.AccessToken));
    }

    [Fact]
    public async Task Calls_without_the_xsrf_header_are_refused()
    {
        var browser = await BrowserAsync();
        var userName = await factory.CreateUserAsync();

        var login = await browser.PostAsync("login", new WebLoginRequest(userName, ApiFactory.TestPassword), sendXsrf: false);
        await browser.SignInAsync(userName);
        var refresh = await browser.PostAsync("refresh", sendXsrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
    }

    [Fact]
    public async Task Refresh_without_a_cookie_is_unauthorized()
    {
        var browser = await BrowserAsync();

        var response = await browser.PostAsync("refresh");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sign_out_ends_the_session_so_the_old_cookie_no_longer_refreshes()
    {
        var browser = await BrowserAsync();
        await browser.SignInAsync(await factory.CreateUserAsync());
        var oldCookie = browser.Cookies["waa_refresh"];

        var logout = await browser.PostAsync("logout");
        browser.Cookies["waa_refresh"] = oldCookie;
        var refresh = await browser.PostAsync("refresh");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    private async Task<Browser> BrowserAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        var browser = new Browser(client);
        var xsrf = await browser.SendAsync(HttpMethod.Get, "/api/v1/auth/web/xsrf", content: null, sendXsrf: false);
        Assert.Equal(HttpStatusCode.NoContent, xsrf.StatusCode);
        Assert.True(browser.Cookies.ContainsKey("XSRF-TOKEN"));
        return browser;
    }

    /// <summary>A tiny cookie jar that behaves like a browser for these endpoints.</summary>
    private sealed class Browser(HttpClient client)
    {
        public HttpClient Client { get; } = client;

        public Dictionary<string, string> Cookies { get; } = [];

        public Task<HttpResponseMessage> PostAsync(string action, object? body = null, bool sendXsrf = true) =>
            SendAsync(HttpMethod.Post, $"/api/v1/auth/web/{action}", body is null ? null : JsonContent.Create(body), sendXsrf);

        public async Task<WebSessionResponse> SignInAsync(string userName)
        {
            var response = await PostAsync("login", new WebLoginRequest(userName, ApiFactory.TestPassword));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<WebSessionResponse>(Ct))!;
        }

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, bool sendXsrf)
        {
            using var request = new HttpRequestMessage(method, url) { Content = content };
            if (Cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", Cookies.Select(c => $"{c.Key}={c.Value}")));
            }

            if (sendXsrf && Cookies.TryGetValue("XSRF-TOKEN", out var xsrf))
            {
                request.Headers.Add("X-XSRF-TOKEN", xsrf);
            }

            var response = await Client.SendAsync(request, Ct);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    var pair = setCookie.Split(';', 2)[0].Split('=', 2);
                    var expired = setCookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
                    if (expired || pair[1].Length == 0)
                    {
                        Cookies.Remove(pair[0]);
                    }
                    else
                    {
                        Cookies[pair[0]] = pair[1];
                    }
                }
            }

            return response;
        }
    }
}
