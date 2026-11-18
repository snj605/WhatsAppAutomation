using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Controllers;
using WhatsAppAutomation.Api.Tests.Infrastructure;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Broadcasts.Queries;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Application.Features.Modules;
using WhatsAppAutomation.Application.Features.Notifications;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Tests.Modules;

/// <summary>Occasions (modules), their dates, and notifications: the rules the website and the app rely on.</summary>
public sealed class ModulesApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Users_make_their_own_occasions_with_one_birthday_at_most_twenty_and_nobody_else_sees_them()
    {
        var client = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        Assert.Empty((await client.GetFromJsonAsync<List<ModuleDto>>("/api/v1/modules", Ct))!);

        var birthday = await CreateAsync(client, "Birthday", "birthday");
        Assert.Equal("birthday", birthday.Kind);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(client, "Birthdays again", "birthday")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(client, " birthday ", "yearly")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "", "yearly")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "Weekly", "weekly")).StatusCode);

        var anniversary = await CreateAsync(client, "Anniversary", "yearly");
        var renamed = await client.PutAsJsonAsync($"/api/v1/modules/{anniversary.Id}", new UpdateModuleRequest("Wedding anniversary", "Couple"), Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var changed = (await renamed.Content.ReadFromJsonAsync<ModuleDto>(Ct))!;
        Assert.Equal(("Wedding anniversary", "Couple"), (changed.Name, changed.ForNameLabel));

        for (var i = 3; i <= ModuleDates.MaxModules; i++)
        {
            await CreateAsync(client, $"Event {i}", "once");
        }

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(client, "One too many", "once")).StatusCode);
        Assert.Equal(ModuleDates.MaxModules, (await client.GetFromJsonAsync<List<ModuleDto>>("/api/v1/modules", Ct))!.Count);

        // Another user sees none of them and cannot open, change or delete them.
        var other = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        Assert.Empty((await other.GetFromJsonAsync<List<ModuleDto>>("/api/v1/modules", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/modules/{anniversary.Id}/days", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/modules/{anniversary.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync($"/api/v1/modules/{anniversary.Id}", new UpdateModuleRequest("Mine", null), Ct)).StatusCode);
    }

    [Fact]
    public async Task Dates_are_added_searched_changed_and_removed_only_for_own_contacts()
    {
        var client = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        var asthi = await CreateAsync(client, "Asthi", "yearly", "In memory of");
        var once = await CreateAsync(client, "Opening", "once");
        var birthday = await CreateAsync(client, "Birthday", "birthday");
        var ramesh = await ContactAsync(client, "Ramesh");
        var sita = await ContactAsync(client, "Sita");

        Assert.Equal(HttpStatusCode.NoContent, (await PutEntryAsync(client, asthi.Id, ramesh, new(5, 3, 2019, "Shantaben"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PutEntryAsync(client, asthi.Id, sita, new(29, 2, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutEntryAsync(client, asthi.Id, sita, new(30, 2, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutEntryAsync(client, once.Id, sita, new(1, 1, null, null))).StatusCode);
        // Birthday dates are set the same way.
        Assert.Equal(HttpStatusCode.NoContent, (await PutEntryAsync(client, birthday.Id, sita, new(1, 1, null, null))).StatusCode);

        var all = (await client.GetFromJsonAsync<ModuleEntriesPage>($"/api/v1/modules/{asthi.Id}/entries", Ct))!;
        Assert.Equal(2, all.Total);
        var found = (await client.GetFromJsonAsync<ModuleEntriesPage>($"/api/v1/modules/{asthi.Id}/entries?search=rames", Ct))!;
        var entry = Assert.Single(found.Items);
        Assert.Equal((ramesh, 5, 3, 2019, "Shantaben"), (entry.ContactId, entry.Day, entry.Month, entry.Year, entry.ForName));

        // Saving again changes the date instead of adding a second one.
        Assert.Equal(HttpStatusCode.NoContent, (await PutEntryAsync(client, asthi.Id, ramesh, new(6, 3, 2019, "Shantaben"))).StatusCode);
        var again = (await client.GetFromJsonAsync<ModuleEntriesPage>($"/api/v1/modules/{asthi.Id}/entries", Ct))!;
        Assert.Equal(2, again.Total);
        Assert.Contains(again.Items, e => e.ContactId == ramesh && e.Day == 6);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/modules/{asthi.Id}/entries/{sita}", Ct)).StatusCode);
        Assert.Equal(1, (await client.GetFromJsonAsync<ModuleEntriesPage>($"/api/v1/modules/{asthi.Id}/entries", Ct))!.Total);

        // Someone else's contact cannot be put in, even into one's own occasion.
        var other = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        var otherModule = await CreateAsync(other, "Asthi", "yearly");
        Assert.Equal(HttpStatusCode.NotFound, (await PutEntryAsync(other, otherModule.Id, ramesh, new(1, 1, null, null))).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_occasion_removes_its_dates_and_stops_its_waiting_wishes()
    {
        var (client, _) = await ConnectedUserAsync();
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));
        var anniversary = await CreateAsync(client, "Anniversary", "yearly");
        var person = await ContactAsync(client, "Kiran");
        Assert.Equal(HttpStatusCode.NoContent, (await PutEntryAsync(client, anniversary.Id, person, new(today.Day, today.Month, 2010, null))).StatusCode);

        var days = (await client.GetFromJsonAsync<List<ModuleDayPersonDto>>($"/api/v1/modules/{anniversary.Id}/days", Ct))!;
        var kiran = Assert.Single(days);
        Assert.Equal((0, 16, "not_sent"), (kiran.DaysUntil, kiran.Years, kiran.Status));

        var wish = await client.PostAsJsonAsync("/api/v1/broadcasts",
            new BroadcastInput("Anniversary", null, null, null, null, null, [person], "Happy anniversary {{name}}", ModuleId: anniversary.Id), Ct);
        Assert.True(wish.IsSuccessStatusCode, await wish.Content.ReadAsStringAsync(Ct));
        var queue = (await client.GetFromJsonAsync<QueueDto>("/api/v1/broadcasts/queue", Ct))!;
        Assert.Equal("occasion", Assert.Single(queue.Items).Kind);
        Assert.Equal("pending", (await client.GetFromJsonAsync<List<ModuleDayPersonDto>>($"/api/v1/modules/{anniversary.Id}/days", Ct))!.Single().Status);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/modules/{anniversary.Id}", Ct)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<QueueDto>("/api/v1/broadcasts/queue", Ct))!.Items);
        Assert.Empty((await client.GetFromJsonAsync<List<ModuleDto>>("/api/v1/modules", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/modules/{anniversary.Id}/entries", Ct)).StatusCode);
    }

    [Fact]
    public async Task Notifications_are_the_users_own_and_can_be_marked_read()
    {
        var userName = await factory.CreateUserAsync(UserKind.TenantAdmin);
        var client = await factory.CreateClientForAsync(userName);
        var userId = await factory.UserIdAsync(userName);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<NotificationSender>();
            await sender.SendAsync(userId, "paused", "Sending paused", "3 messages failed in a row.", Ct);
            await sender.SendAsync(userId, "occasion", "Birthday: 2 wishes started", null, Ct);
        }

        var page = (await client.GetFromJsonAsync<NotificationsPage>("/api/v1/notifications", Ct))!;
        Assert.Equal(2, page.Unread);
        Assert.Equal("Birthday: 2 wishes started", page.Items[0].Title);
        Assert.All(page.Items, n => Assert.False(n.Read));

        var other = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        Assert.Empty((await other.GetFromJsonAsync<NotificationsPage>("/api/v1/notifications", Ct))!.Items);
        // Marking someone else's notification read changes nothing for its owner.
        Assert.True((await other.PostAsJsonAsync("/api/v1/notifications/read", new MarkReadRequest([page.Items[0].Id]), Ct)).IsSuccessStatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<NotificationsPage>("/api/v1/notifications", Ct))!.Unread);

        Assert.True((await client.PostAsJsonAsync("/api/v1/notifications/read", new MarkReadRequest(null), Ct)).IsSuccessStatusCode);
        var read = (await client.GetFromJsonAsync<NotificationsPage>("/api/v1/notifications", Ct))!;
        Assert.Equal(0, read.Unread);
        Assert.All(read.Items, n => Assert.True(n.Read));
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string name, string kind, string? label = null) =>
        client.PostAsJsonAsync("/api/v1/modules", new CreateModuleRequest(name, kind, label), Ct);

    private static async Task<ModuleDto> CreateAsync(HttpClient client, string name, string kind, string? label = null)
    {
        var response = await PostAsync(client, name, kind, label);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ModuleDto>(Ct))!;
    }

    private static Task<HttpResponseMessage> PutEntryAsync(HttpClient client, Guid moduleId, Guid contactId, ModuleEntryRequest entry) =>
        client.PutAsJsonAsync($"/api/v1/modules/{moduleId}/entries/{contactId}", entry, Ct);

    private static async Task<Guid> ContactAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/contacts", new ContactInput(ApiFactory.UniquePhone(), name, name, null, null, "en", null, null, null, null, null), Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ContactDto>(Ct))!.Id;
    }

    private async Task<(HttpClient Client, Guid UserId)> ConnectedUserAsync()
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
