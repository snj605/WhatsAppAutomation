using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Tests.Infrastructure;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Application.Features.Contacts.Queries;
using WhatsAppAutomation.Application.Features.Files;
using WhatsAppAutomation.Application.Features.Templates;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Tests.Broadcasts;

// Every test class that runs the sender shares this collection: the sender works on all users at once,
// so two such classes running in parallel would send each other's messages and make the counts unsure.
[Collection("Sender")]
public sealed class BroadcastsApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_preview_picks_each_contacts_language_fills_in_real_names_and_lists_who_is_skipped()
    {
        var (client, _) = await ConnectedOwnerAsync();
        var elders = await CategoryAsync(client, "Elders");
        await ContactAsync(client, "+919825011111", elders, "gu", nameGu: "રમેશભાઈ", salutation: "Pujya", saved: "Daddy");
        await ContactAsync(client, "+919825022222", elders, "hi", nameHi: "सुरेश", nameEn: "Suresh", saved: "Kaka");
        await ContactAsync(client, "+919825033333", elders, "en", nameEn: "Riya");
        await ContactAsync(client, "+919825044444", elders, "gu", nameGu: "કોકિલા", saved: "Mummy");

        // No Hindi text: the Hindi contact gets the default language (English) with their English name.
        var input = Input([elders], en: "Happy Diwali {name}!", gu: "{salutation} {name}, દિવાળીની શુભેચ્છા");
        var preview = await PreviewAsync(client, input);

        Assert.Equal(4, preview.Total);
        Assert.Equal(4, preview.WillSend);
        Assert.Equal(2, preview.ByLanguage["en"]);
        Assert.Equal(2, preview.ByLanguage["gu"]);
        Assert.Contains(preview.Samples, s => s.Language == "gu" && s.Text is "Pujya રમેશભાઈ, દિવાળીની શુભેચ્છા" or "કોકિલા, દિવાળીની શુભેચ્છા");

        // Only Hindi text: only the Hindi contact can get it; the others have no text in their language or the default.
        var hindiOnly = await PreviewAsync(client, Input([elders], hi: "नमस्ते {name}"));
        Assert.Equal(1, hindiOnly.WillSend);
        Assert.Equal("नमस्ते सुरेश", Assert.Single(hindiOnly.Samples).Text);
        Assert.Contains(hindiOnly.Skipped, s => s.Name == "Riya" && s.Reason == "No text in English, their language.");
        Assert.Contains(hindiOnly.Skipped, s => s.Name == "Daddy" && s.Reason.StartsWith("No text in Gujarati (their language) or English", StringComparison.Ordinal));

        // English text that needs a name, for a Gujarati contact without an English name and no Gujarati text.
        var englishOnly = await PreviewAsync(client, Input([elders], en: "Hi {name}"));
        Assert.Contains(englishOnly.Skipped, s => s.Name == "Daddy" && s.Reason == "Needs details: no English name.");
    }

    [Fact]
    public async Task A_broadcast_sends_one_personal_message_per_run_and_completes()
    {
        var (client, _) = await ConnectedOwnerAsync();
        var family = await CategoryAsync(client, "Family");
        await ContactAsync(client, "+919825011111", family, "gu", nameGu: "રમેશભાઈ", salutation: "Pujya");
        await ContactAsync(client, "+919825022222", family, "en", nameEn: "Riya");

        var created = await CreateAsync(client, Input([family], en: "Hello {name}", gu: "{salutation} {name} જય શ્રી કૃષ્ણ"));
        Assert.Equal("sending", created.Status);
        Assert.Equal(2, created.Counts.Pending);

        Assert.Equal(1, await RunSenderAsync());
        Assert.Equal(1, await RunSenderAsync());
        Assert.Equal(0, await RunSenderAsync());

        var details = (await client.GetFromJsonAsync<BroadcastDetailsDto>($"/api/v1/broadcasts/{created.Id}", Ct))!;
        Assert.Equal("completed", details.Summary.Status);
        Assert.Equal(2, details.Summary.Counts.Sent);
        Assert.Contains(details.Recipients, r => r.Text == "Pujya રમેશભાઈ જય શ્રી કૃષ્ણ" && r.Status == "sent");
        Assert.Contains(details.Recipients, r => r.Text == "Hello Riya");
        Assert.Contains(factory.Gateway.Calls, c => c.Contains("+919825011111", StringComparison.Ordinal));

        var pacing = (await client.GetFromJsonAsync<PacingDto>("/api/v1/broadcasts/pacing", Ct))!;
        Assert.Equal(2, pacing.SentToday);
        Assert.Equal(20, pacing.DailyCap);
        Assert.Equal(1, pacing.WarmUpWeek);
    }

    [Fact]
    public async Task A_contact_can_get_another_broadcast_right_away_by_default()
    {
        // Recipients are people already in the sender's phone book, so the per-contact gap is off by default
        // (Broadcasting:ContactGapDays = 0).
        var (client, _) = await ConnectedOwnerAsync();
        var family = await CategoryAsync(client, "Family");
        await ContactAsync(client, "+919825011111", family, "en", nameEn: "Riya");
        await CreateAsync(client, Input([family], en: "First {name}"));
        await RunSenderAsync();

        var preview = await PreviewAsync(client, Input([family], en: "Second {name}"));
        Assert.Equal(1, preview.WillSend);
        Assert.Empty(preview.Skipped);
        var second = await client.PostAsJsonAsync("/api/v1/broadcasts", Input([family], en: "Second {name}"), Ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Three_failures_in_a_row_pause_the_broadcast_until_a_person_resumes_it_and_cancel_skips_the_rest()
    {
        var (client, _) = await ConnectedOwnerAsync();
        var list = await CategoryAsync(client, "Bad list");
        await ContactAsync(client, FakeWhatsAppGateway.NotOnWhatsAppNumber, list, "en", nameEn: "A");
        await ContactAsync(client, "+15550000002", list, "en", nameEn: "B");
        await ContactAsync(client, "+15550000003", list, "en", nameEn: "C");
        await ContactAsync(client, "+15550000004", list, "en", nameEn: "D");
        await ContactAsync(client, "+15550000005", list, "en", nameEn: "E");
        factory.Gateway.FailNumbers.Add("+15550000002");
        factory.Gateway.FailNumbers.Add("+15550000003");

        var created = await CreateAsync(client, Input([list], en: "Hi {name}"));
        await RunSenderAsync();
        await RunSenderAsync();
        await RunSenderAsync();

        var paused = await SummaryAsync(client, created.Id);
        Assert.Equal("paused", paused.Status);
        Assert.Contains("failed in a row", paused.PauseReason, StringComparison.Ordinal);
        Assert.Equal(3, paused.Counts.Failed);
        Assert.Equal(0, await RunSenderAsync());

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/broadcasts/{created.Id}/resume", null, Ct)).StatusCode);
        Assert.Equal(1, await RunSenderAsync());
        var cancel = await client.PostAsync($"/api/v1/broadcasts/{created.Id}/cancel", null, Ct);
        var cancelled = (await cancel.Content.ReadFromJsonAsync<BroadcastSummaryDto>(Ct))!;
        Assert.Equal("cancelled", cancelled.Status);
        Assert.Equal(1, cancelled.Counts.Skipped);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/broadcasts/{created.Id}/resume", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_daily_limit_of_a_newly_linked_number_stops_sending_for_the_day()
    {
        var (client, _) = await ConnectedOwnerAsync();
        var list = await CategoryAsync(client, "Many");
        for (var i = 0; i < 22; i++)
        {
            await ContactAsync(client, $"+9198250{i:00000}", list, "en", nameEn: $"P{i}");
        }

        var created = await CreateAsync(client, Input([list], en: "Hi {name}"));
        for (var i = 0; i < 25; i++)
        {
            await RunSenderAsync();
        }

        var summary = await SummaryAsync(client, created.Id);
        Assert.Equal(20, summary.Counts.Sent);
        Assert.Equal(2, summary.Counts.Pending);
        var pacing = (await client.GetFromJsonAsync<PacingDto>("/api/v1/broadcasts/pacing", Ct))!;
        Assert.Contains("limit", pacing.Waiting, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Senders_running_at_the_same_moment_never_send_one_person_two_messages()
    {
        var (client, _) = await ConnectedOwnerAsync();
        var list = await CategoryAsync(client, "Same moment");
        var phones = Enumerable.Range(0, 6).Select(i => $"+9198333{i:00000}").ToList();
        foreach (var phone in phones)
        {
            await ContactAsync(client, phone, list, "en", nameEn: "P");
        }

        var created = await CreateAsync(client, Input([list], en: "Hi {name}"));
        for (var i = 0; i < 8; i++)
        {
            await Task.WhenAll(RunSenderAsync(), RunSenderAsync(), RunSenderAsync());
        }

        var summary = await SummaryAsync(client, created.Id);
        Assert.Equal(6, summary.Counts.Sent);
        foreach (var phone in phones)
        {
            Assert.Single(factory.Gateway.Calls, c => c.StartsWith("send ", StringComparison.Ordinal) && c.Contains(phone, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Broadcasts_pause_when_the_number_is_not_linked_and_need_their_permission()
    {
        var userName = await factory.CreateUserAsync(UserKind.TenantAdmin);
        var client = await factory.CreateClientForAsync(userName);
        var list = await CategoryAsync(client, "List");
        await ContactAsync(client, "+919825011111", list, "en", nameEn: "Riya");
        var created = await CreateAsync(client, Input([list], en: "Hi {name}"));

        await RunSenderAsync();

        var summary = await SummaryAsync(client, created.Id);
        Assert.Equal("paused", summary.Status);
        Assert.Contains("not linked", summary.PauseReason, StringComparison.Ordinal);

        // A member without the Broadcasts switch may send to one contact, not to a list.
        var memberName = await factory.CreateUserAsync();
        var member = await factory.CreateClientForAsync(memberName);
        var memberList = await CategoryAsync(member, "Mine");
        var refused = await member.PostAsJsonAsync("/api/v1/broadcasts", Input([memberList], en: "Hi {name}"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var other = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/broadcasts/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task An_opted_out_contact_is_skipped_until_allowed_again()
    {
        var (client, userId) = await ConnectedOwnerAsync();
        var list = await CategoryAsync(client, "Friends");
        await ContactAsync(client, "+919825055555", list, "en", nameEn: "Riya");
        await ContactAsync(client, "+919825066666", list, "en", nameEn: "Asha");

        var riyaId = Guid.Empty;
        await factory.WithDbAsync(db =>
        {
            riyaId = db.Contacts.Single(c => c.UserId == userId && c.PhoneNumber == "+919825055555").Id;
            return Task.CompletedTask;
        });

        var stop = await client.PutAsJsonAsync($"/api/v1/contacts/{riyaId}/opt-out", new SetOptOutRequest(true), Ct);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);

        // Both placeholder styles work; Riya is skipped because she opted out.
        var preview = await PreviewAsync(client, Input([list], en: "Hi {{name}}"));
        Assert.Equal(1, preview.WillSend);
        Assert.Equal("Hi Asha", Assert.Single(preview.Samples).Text);
        Assert.Contains(preview.Skipped, s => s.Name == "Riya" && s.Reason == BroadcastPlanner.OptedOutReason);

        var contact = (await client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{riyaId}", Ct))!;
        Assert.NotNull(contact.OptedOutAt);

        var allow = await client.PutAsJsonAsync($"/api/v1/contacts/{riyaId}/opt-out", new SetOptOutRequest(false), Ct);
        Assert.Equal(HttpStatusCode.OK, allow.StatusCode);
        Assert.Null((await allow.Content.ReadFromJsonAsync<ContactDto>(Ct))!.OptedOutAt);
        Assert.Equal(2, (await PreviewAsync(client, Input([list], en: "Hi {name}"))).WillSend);
    }

    [Fact]
    public async Task Contacts_without_a_category_can_be_picked_one_by_one()
    {
        var (client, userId) = await ConnectedOwnerAsync();
        await ContactAsync(client, "+919825077777", null, "en", nameEn: "Meera");

        var meeraId = Guid.Empty;
        await factory.WithDbAsync(db =>
        {
            var meera = db.Contacts.Single(c => c.UserId == userId && c.PhoneNumber == "+919825077777");
            Assert.Null(meera.CategoryId);
            meeraId = meera.Id;
            return Task.CompletedTask;
        });

        var preview = await PreviewAsync(client, new BroadcastInput("Just Meera", "Hello {{name}}", null, null, null, null, [meeraId]));
        Assert.Equal(1, preview.WillSend);
        Assert.Equal("Hello Meera", Assert.Single(preview.Samples).Text);
    }

    [Fact]
    public async Task One_message_for_everyone_fills_each_persons_name_in_the_chosen_language()
    {
        var (client, _) = await ConnectedOwnerAsync();
        var family = await CategoryAsync(client, "Family");
        await ContactAsync(client, "+919825011112", family, "gu", nameGu: "રમેશભાઈ", nameEn: "Rameshbhai", salutation: "Pujya");
        await ContactAsync(client, "+919825022223", family, "en", nameEn: "Riya");

        var input = new BroadcastInput("Diwali", null, null, null, [family], null, null, "{{salutation}} {{name_gu}} શુભ દીપાવલી");
        var preview = await PreviewAsync(client, input);

        Assert.Equal(1, preview.WillSend);
        Assert.Equal("Pujya રમેશભાઈ શુભ દીપાવલી", Assert.Single(preview.Samples).Text);
        Assert.Contains(preview.Skipped, s => s.Name == "Riya" && s.Reason == "Needs details: no Gujarati name.");

        var created = await CreateAsync(client, new BroadcastInput("Diwali", null, null, null, [family], null, null, "Happy Diwali {{name}}"));
        Assert.Equal(2, created.Counts.Pending);
        var details = (await client.GetFromJsonAsync<BroadcastDetailsDto>($"/api/v1/broadcasts/{created.Id}", Ct))!;
        Assert.Equal("Happy Diwali {{name}}", details.Text);
        Assert.Contains(details.Recipients, r => r.Text == "Happy Diwali Riya");
    }

    [Fact]
    public async Task Everyone_sends_to_all_of_the_users_contacts_and_nobody_elses()
    {
        var (client, _) = await ConnectedOwnerAsync();
        await ContactAsync(client, "+919825031111", null, "en", nameEn: "Asha");
        await ContactAsync(client, "+919825032222", null, "en", nameEn: "Bina");
        var (other, _) = await ConnectedOwnerAsync();
        await ContactAsync(other, "+919825033333", null, "en", nameEn: "Chetan");

        var preview = await PreviewAsync(client, new BroadcastInput("All", null, null, null, null, null, null, "Hi {{name}}", AllContacts: true));

        Assert.Equal(2, preview.Total);
        Assert.Equal(2, preview.WillSend);
        Assert.DoesNotContain(preview.Samples, s => s.ContactName == "Chetan");
    }

    [Fact]
    public async Task A_file_is_stored_once_sent_with_every_message_and_freed_when_the_broadcast_is_done()
    {
        var (client, userId) = await ConnectedOwnerAsync();
        await ContactAsync(client, "+919825041111", null, "en", nameEn: "Asha");
        await ContactAsync(client, "+919825042222", null, "en", nameEn: "Bina");

        using var form = new MultipartFormDataContent();
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 1, 2, 3 }; // "%PDF…"
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(content, "file", "..\\Diwali offer.pdf");
        var uploaded = await client.PostAsync("/api/v1/files", form, Ct);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var file = (await uploaded.Content.ReadFromJsonAsync<FileDto>(Ct))!;
        Assert.Equal("Diwali offer.pdf", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);

        // The gateway can fetch it with the secret only.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/internal/files/{file.Id}", Ct)).StatusCode);
        using (var fetch = new HttpRequestMessage(HttpMethod.Get, $"/internal/files/{file.Id}"))
        {
            fetch.Headers.Add("X-Gateway-Secret", ApiFactory.GatewaySecret);
            var fetched = await factory.CreateClient().SendAsync(fetch, Ct);
            Assert.Equal(bytes, await fetched.Content.ReadAsByteArrayAsync(Ct));
        }

        var created = await CreateAsync(
            client, new BroadcastInput("Offer", null, null, null, null, null, null, "Hi {{name}}, our offer", AllContacts: true, AttachmentId: file.Id));
        await RunSenderAsync();
        await RunSenderAsync();

        Assert.Equal(2, factory.Gateway.Calls.Count(c => c.Contains($"file {file.Id} Diwali offer.pdf application/pdf", StringComparison.Ordinal)));
        var details = (await client.GetFromJsonAsync<BroadcastDetailsDto>($"/api/v1/broadcasts/{created.Id}", Ct))!;
        Assert.Equal("completed", details.Summary.Status);
        Assert.Equal("Diwali offer.pdf", details.Attachment!.FileName);

        // The bytes are freed once nothing needs them (the cleanup runs at most once a minute, so call it directly).
        await factory.WithDbAsync(async db =>
        {
            await WhatsAppAutomation.Application.Features.Files.FileCleanup.RunAsync(db, DateTimeOffset.UtcNow, Ct);
            Assert.Null(db.StoredFiles.Single(f => f.Id == file.Id).Data);
        });
    }

    [Fact]
    public async Task Programs_cannot_be_attached_and_another_users_file_cannot_be_used()
    {
        var (client, _) = await ConnectedOwnerAsync();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "file", "setup.exe");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/files", form, Ct)).StatusCode);

        var (other, _) = await ConnectedOwnerAsync();
        using var otherForm = new MultipartFormDataContent();
        otherForm.Add(new ByteArrayContent([1, 2, 3]), "file", "photo.jpg");
        var otherFile = (await (await other.PostAsync("/api/v1/files", otherForm, Ct)).Content.ReadFromJsonAsync<FileDto>(Ct))!;

        await ContactAsync(client, "+919825043333", null, "en", nameEn: "Chetan");
        var response = await client.PostAsJsonAsync(
            "/api/v1/broadcasts", new BroadcastInput("x", null, null, null, null, null, null, "Hi", AllContacts: true, AttachmentId: otherFile.Id), Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Todays_birthdays_are_listed_with_the_new_age()
    {
        var (client, _) = await ConnectedOwnerAsync();
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));
        var created = await client.PostAsJsonAsync(
            "/api/v1/contacts",
            new ContactInput("+919825088888", "Kiran", "Kiran", null, null, "en", null, null, new BirthdayDto(today.Day, today.Month, 1990), null, null),
            Ct);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync(Ct));

        var list = (await client.GetFromJsonAsync<List<UpcomingBirthdayDto>>("/api/v1/birthdays?days=0", Ct))!;

        var kiran = Assert.Single(list);
        Assert.Equal(0, kiran.DaysUntil);
        Assert.Equal(today.Year - 1990, kiran.TurnsAge);
        Assert.False(kiran.OptedOut);
        Assert.Equal("not_sent", kiran.Status);
    }

    [Fact]
    public async Task Automatic_birthday_wishes_switched_on_after_their_time_start_tomorrow()
    {
        var (client, _) = await ConnectedOwnerAsync();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var saved = await client.PostAsJsonAsync("/api/v1/templates", new SaveTemplateRequest("Birthday", "Happy birthday {{name}}"), Ct);
        var template = (await saved.Content.ReadFromJsonAsync<TemplateDto>(Ct))!;

        // A time is needed, and a template, before they can be switched on.
        var missing = await client.PutAsJsonAsync("/api/v1/birthdays/settings", new BirthdaySettingsRequest(true, "07:15", null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        // 00:00 has always passed today, so the first automatic wishes are tomorrow, not right now.
        var response = await client.PutAsJsonAsync("/api/v1/birthdays/settings", new BirthdaySettingsRequest(true, "00:00", template.Id), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = (await response.Content.ReadFromJsonAsync<BirthdaySettingsDto>(Ct))!;
        Assert.True(settings.Enabled);
        Assert.Equal("00:00", settings.Time);
        Assert.Equal("Birthday", settings.TemplateName);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var next = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(settings.NextRunAt!.Value, zone).DateTime);
        Assert.Equal(today.AddDays(1), next);

        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<BirthdayAutoSender>().RunOnceAsync(Ct));
        var again = (await client.GetFromJsonAsync<BirthdaySettingsDto>("/api/v1/birthdays/settings", Ct))!;
        Assert.Equal(template.Id, again.TemplateId);
    }

    private static BroadcastInput Input(Guid[] categories, string? en = null, string? hi = null, string? gu = null) =>
        new("Test broadcast", en, hi, gu, categories, null, null);

    private async Task<int> RunSenderAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BroadcastDispatcher>().RunOnceAsync(Ct);
    }

    private static async Task<BroadcastPreviewDto> PreviewAsync(HttpClient client, BroadcastInput input)
    {
        var response = await client.PostAsJsonAsync("/api/v1/broadcasts/preview", input, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BroadcastPreviewDto>(Ct))!;
    }

    private static async Task<BroadcastSummaryDto> CreateAsync(HttpClient client, BroadcastInput input)
    {
        var response = await client.PostAsJsonAsync("/api/v1/broadcasts", input, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BroadcastSummaryDto>(Ct))!;
    }

    private static async Task<BroadcastSummaryDto> SummaryAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<BroadcastDetailsDto>($"/api/v1/broadcasts/{id}", Ct))!.Summary;

    private static async Task<Guid> CategoryAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/categories", new SaveCategoryRequest(name, "#10b981"), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(Ct))!.Id;
    }

    private static async Task ContactAsync(
        HttpClient client, string phone, Guid? categoryId, string language, string? nameEn = null, string? nameHi = null, string? nameGu = null,
        string? salutation = null, string? saved = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/contacts",
            new ContactInput(phone, saved, nameEn, nameHi, nameGu, language, categoryId, salutation, null, null, null),
            Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<(HttpClient Client, Guid UserId)> ConnectedOwnerAsync()
    {
        var userName = await factory.CreateUserAsync(UserKind.TenantAdmin);
        var client = await factory.CreateClientForAsync(userName);
        var userId = await factory.UserIdAsync(userName);
        (await client.PostAsJsonAsync("/api/v1/whatsapp/link", new LinkWhatsAppRequest("qr", null), Ct)).EnsureSuccessStatusCode();
        var accountId = Guid.Empty;
        await factory.WithDbAsync(db =>
        {
            accountId = db.WhatsAppAccounts.Single(a => a.UserId == userId).Id;
            return Task.CompletedTask;
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/whatsapp/events")
        {
            Content = JsonContent.Create(new { type = "connection", accountId, status = "connected", reason = "linked", phoneNumber = ApiFactory.UniquePhone() }),
        };
        request.Headers.Add("X-Gateway-Secret", ApiFactory.GatewaySecret);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().SendAsync(request, Ct)).StatusCode);
        return (client, userId);
    }
}
