using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Tests.Infrastructure;
using WhatsAppAutomation.Application.Features.WhatsApp;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Api.Tests.WhatsApp;

/// <summary>
/// The WhatsApp API with a fake gateway. The gateway's events are posted to /internal the same way
/// the real gateway posts them.
/// </summary>
public sealed class WhatsAppApiTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_user_has_no_linked_number_and_anonymous_gets_401()
    {
        var (client, _) = await OwnerAsync();

        var account = await GetAccountAsync(client);

        Assert.Equal(WhatsAppConnectionStatus.NotLinked, account.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/whatsapp", Ct)).StatusCode);
    }

    [Fact]
    public async Task Linking_and_sending_need_their_permissions()
    {
        var sender = await factory.CreateClientForAsync(await factory.CreateUserAsync(SystemRoles.Sender));

        var link = await sender.PostAsJsonAsync("/api/v1/whatsapp/link", new LinkWhatsAppRequest("qr", null), Ct);
        var send = await sender.PostAsJsonAsync("/api/v1/whatsapp/messages", new SendTestMessageRequest("+919876543210", "Hi"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, link.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, send.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sender.GetAsync("/api/v1/whatsapp", Ct)).StatusCode);
    }

    [Fact]
    public async Task Linking_with_a_QR_shows_the_codes_live_and_ends_connected()
    {
        var (client, userId) = await OwnerAsync();

        var link = await client.PostAsJsonAsync("/api/v1/whatsapp/link", new LinkWhatsAppRequest("qr", null), Ct);
        Assert.Equal(HttpStatusCode.Accepted, link.StatusCode);
        var accountId = await AccountIdAsync(userId);
        Assert.Contains($"link {accountId} Qr ", factory.Gateway.Calls);
        Assert.Equal(WhatsAppConnectionStatus.Linking, (await GetAccountAsync(client)).Status);

        await PostEventAsync(new { type = "qr", accountId, qr = "2@qr-data" });
        Assert.Contains(factory.Notifier.Sent, n => n.UserId == userId && n.Kind == "qr" && (string)n.Payload == "2@qr-data");

        await PostEventAsync(new { type = "connection", accountId, status = "connected", reason = "linked", phoneNumber = "+919876543210", name = "Dhaval" });
        var account = await GetAccountAsync(client);
        Assert.Equal(WhatsAppConnectionStatus.Connected, account.Status);
        Assert.Equal("+919876543210", account.PhoneNumber);
        Assert.Equal("Dhaval", account.ProfileName);
        Assert.NotNull(account.LinkedAt);

        var again = await client.PostAsJsonAsync("/api/v1/whatsapp/link", new LinkWhatsAppRequest("qr", null), Ct);
        await AssertProblemAsync(again, HttpStatusCode.Conflict, "whatsapp.already_linked");
    }

    [Fact]
    public async Task Linking_with_a_phone_code_needs_a_valid_international_number()
    {
        var (client, userId) = await OwnerAsync();

        var bad = await client.PostAsJsonAsync("/api/v1/whatsapp/link", new LinkWhatsAppRequest("code", "98765 43210"), Ct);
        await AssertProblemAsync(bad, HttpStatusCode.BadRequest, "whatsapp.invalid_phone");
        var badMethod = await client.PostAsJsonAsync("/api/v1/whatsapp/link", new LinkWhatsAppRequest("sms", null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badMethod.StatusCode);

        var ok = await client.PostAsJsonAsync("/api/v1/whatsapp/link", new LinkWhatsAppRequest("code", "+91 98765-43210"), Ct);
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        var accountId = await AccountIdAsync(userId);
        Assert.Contains($"link {accountId} PhoneCode +919876543210", factory.Gateway.Calls);

        await PostEventAsync(new { type = "pairing_code", accountId, code = "ABCD1234" });
        Assert.Contains(factory.Notifier.Sent, n => n.UserId == userId && n.Kind == "pairingCode" && (string)n.Payload == "ABCD1234");
    }

    [Fact]
    public async Task When_the_gateway_is_down_linking_answers_503()
    {
        var (client, _) = await OwnerAsync();

        var link = await client.PostAsJsonAsync(
            "/api/v1/whatsapp/link", new LinkWhatsAppRequest("code", FakeWhatsAppGateway.GatewayDownNumber), Ct);

        await AssertProblemAsync(link, HttpStatusCode.ServiceUnavailable, "whatsapp.gateway_unavailable");
    }

    [Fact]
    public async Task Gateway_events_need_the_secret_and_a_known_account()
    {
        var anonymous = factory.CreateClient();
        var noSecret = await anonymous.PostAsJsonAsync(
            "/internal/whatsapp/events", new { type = "qr", accountId = Guid.NewGuid(), qr = "x" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, noSecret.StatusCode);

        using var wrong = new HttpRequestMessage(HttpMethod.Post, "/internal/whatsapp/events")
        {
            Content = JsonContent.Create(new { type = "qr", accountId = Guid.NewGuid(), qr = "x" }),
        };
        wrong.Headers.Add("X-Gateway-Secret", "not-the-secret-not-the-secret-not-the-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(wrong, Ct)).StatusCode);

        var unknown = await PostEventAsync(new { type = "qr", accountId = Guid.NewGuid(), qr = "x" }, ensureSuccess: false);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task A_test_message_is_sent_and_its_ticks_only_move_forward()
    {
        var (client, userId) = await ConnectedOwnerAsync();

        var sending = await client.PostAsJsonAsync(
            "/api/v1/whatsapp/messages", new SendTestMessageRequest("+91 98765 43210", "Hello from the app"), Ct);
        Assert.Equal(HttpStatusCode.OK, sending.StatusCode);
        var sent = (await sending.Content.ReadFromJsonAsync<OutgoingMessageDto>(Json, Ct))!;
        Assert.Equal(OutgoingMessageStatus.Sent, sent.Status);
        Assert.Equal("+919876543210", sent.ToPhoneNumber);
        Assert.Contains(factory.Gateway.Calls, c => c.EndsWith($"+919876543210 {sent.Id}", StringComparison.Ordinal));

        var accountId = await AccountIdAsync(userId);
        await PostEventAsync(new { type = "message_status", accountId, reference = sent.Id, status = "read", at = DateTimeOffset.UtcNow });
        await PostEventAsync(new { type = "message_status", accountId, reference = sent.Id, status = "delivered", at = DateTimeOffset.UtcNow });

        var message = Assert.Single(await GetMessagesAsync(client));
        Assert.Equal(OutgoingMessageStatus.Read, message.Status);
        Assert.NotNull(message.DeliveredAt);
        Assert.NotNull(message.ReadAt);
        Assert.Contains(factory.Notifier.Sent, n => n.UserId == userId && n.Kind == "message");
    }

    [Fact]
    public async Task A_number_not_on_WhatsApp_is_refused_and_recorded_as_failed()
    {
        var (client, _) = await ConnectedOwnerAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/whatsapp/messages", new SendTestMessageRequest(FakeWhatsAppGateway.NotOnWhatsAppNumber, "Hi"), Ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "whatsapp.not_on_whatsapp");
        var message = Assert.Single(await GetMessagesAsync(client));
        Assert.Equal(OutgoingMessageStatus.Failed, message.Status);
        Assert.Equal("whatsapp.not_on_whatsapp", message.ErrorCode);
    }

    [Fact]
    public async Task Sending_needs_a_connected_number_and_a_valid_phone_number()
    {
        var (client, _) = await OwnerAsync();

        var notLinked = await client.PostAsJsonAsync("/api/v1/whatsapp/messages", new SendTestMessageRequest("+919876543210", "Hi"), Ct);
        await AssertProblemAsync(notLinked, HttpStatusCode.Conflict, "whatsapp.not_connected");

        var (connected, _) = await ConnectedOwnerAsync();
        var badNumber = await connected.PostAsJsonAsync("/api/v1/whatsapp/messages", new SendTestMessageRequest("12345", "Hi"), Ct);
        await AssertProblemAsync(badNumber, HttpStatusCode.BadRequest, "whatsapp.invalid_phone");
        var emptyText = await connected.PostAsJsonAsync("/api/v1/whatsapp/messages", new SendTestMessageRequest("+919876543210", "  "), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, emptyText.StatusCode);
    }

    [Fact]
    public async Task Users_see_only_their_own_messages()
    {
        var (first, _) = await ConnectedOwnerAsync();
        var (second, _) = await ConnectedOwnerAsync();

        await first.PostAsJsonAsync("/api/v1/whatsapp/messages", new SendTestMessageRequest("+919876543210", "Only mine"), Ct);

        Assert.Single(await GetMessagesAsync(first));
        Assert.Empty(await GetMessagesAsync(second));
    }

    [Fact]
    public async Task Unlinking_clears_the_number()
    {
        var (client, userId) = await ConnectedOwnerAsync();

        var response = await client.PostAsync("/api/v1/whatsapp/unlink", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = await GetAccountAsync(client);
        Assert.Equal(WhatsAppConnectionStatus.NotLinked, account.Status);
        Assert.Null(account.PhoneNumber);
        Assert.Contains($"unlink {await AccountIdAsync(userId)}", factory.Gateway.Calls);
    }

    [Fact]
    public async Task Logged_out_from_the_phone_clears_the_number_and_allows_linking_again()
    {
        var (client, userId) = await ConnectedOwnerAsync();
        var accountId = await AccountIdAsync(userId);

        await PostEventAsync(new { type = "connection", accountId, status = "logged_out", reason = "logged_out" });

        var account = await GetAccountAsync(client);
        Assert.Equal(WhatsAppConnectionStatus.LoggedOut, account.Status);
        Assert.Null(account.PhoneNumber);
        var relink = await client.PostAsJsonAsync("/api/v1/whatsapp/link", new LinkWhatsAppRequest("qr", null), Ct);
        Assert.Equal(HttpStatusCode.Accepted, relink.StatusCode);
    }

    private async Task<(HttpClient Client, Guid UserId)> OwnerAsync()
    {
        var userName = await factory.CreateUserAsync(SystemRoles.Owner);
        return (await factory.CreateClientForAsync(userName), await factory.UserIdAsync(userName));
    }

    private async Task<(HttpClient Client, Guid UserId)> ConnectedOwnerAsync()
    {
        var (client, userId) = await OwnerAsync();
        (await client.PostAsJsonAsync("/api/v1/whatsapp/link", new LinkWhatsAppRequest("qr", null), Ct)).EnsureSuccessStatusCode();
        await PostEventAsync(new
        {
            type = "connection", accountId = await AccountIdAsync(userId), status = "connected", reason = "linked", phoneNumber = "+911111111111",
        });
        return (client, userId);
    }

    private async Task<Guid> AccountIdAsync(Guid userId)
    {
        var id = Guid.Empty;
        await factory.WithDbAsync(db =>
        {
            id = db.WhatsAppAccounts.Single(a => a.UserId == userId).Id;
            return Task.CompletedTask;
        });
        return id;
    }

    private async Task<HttpResponseMessage> PostEventAsync(object body, bool ensureSuccess = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/whatsapp/events") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Gateway-Secret", ApiFactory.GatewaySecret);
        var response = await factory.CreateClient().SendAsync(request, Ct);
        if (ensureSuccess)
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        return response;
    }

    private static async Task<WhatsAppAccountDto> GetAccountAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<WhatsAppAccountDto>("/api/v1/whatsapp", Json, Ct))!;

    private static async Task<List<OutgoingMessageDto>> GetMessagesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<OutgoingMessageDto>>("/api/v1/whatsapp/messages", Json, Ct))!;

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }
}
