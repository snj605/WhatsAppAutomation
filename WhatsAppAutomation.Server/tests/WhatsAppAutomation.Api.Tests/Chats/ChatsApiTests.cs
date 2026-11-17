using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Tests.Infrastructure;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Chats;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Tests.Chats;

[Collection("Sender")]
public sealed class ChatsApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Groups_and_categories_are_chats_and_a_message_sent_to_one_shows_in_its_chat()
    {
        var (client, _) = await ConnectedOwnerAsync();
        var family = await PostAsync<CategoryDto>(client, "/api/v1/categories", new SaveCategoryRequest("Family", "#10b981"));
        var friends = await PostAsync<GroupDto>(client, "/api/v1/groups", new SaveGroupRequest("Friends"));
        var asha = await ContactAsync(client, "+919825051111", "Asha");
        var bina = await ContactAsync(client, "+919825052222", "Bina");
        await PostAsync<GroupDto>(client, $"/api/v1/groups/{friends.Id}/members", new GroupMembersRequest([asha, bina], true));
        var withMember = await PostAsync<CategoryDto>(client, $"/api/v1/categories/{family.Id}/members", new GroupMembersRequest([asha], true));
        Assert.Equal(1, withMember.ContactCount);

        var chats = (await client.GetFromJsonAsync<List<ChatListItemDto>>("/api/v1/chats", Ct))!;
        Assert.Contains(chats, c => c.Kind == "group" && c.Id == friends.Id && c.MemberCount == 2 && c.Last == null);
        Assert.Contains(chats, c => c.Kind == "category" && c.Id == family.Id && c.Color == "#10b981" && c.MemberCount == 1);

        var sent = await client.PostAsJsonAsync(
            "/api/v1/broadcasts", new BroadcastInput("Hello friends", null, null, null, null, [friends.Id], null, "Hi {{name}}"), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        var chat = (await client.GetFromJsonAsync<TargetChatDto>($"/api/v1/chats/group/{friends.Id}", Ct))!;
        var message = Assert.Single(chat.Messages);
        Assert.Equal("Hi {{name}}", message.Text);
        Assert.Equal(2, message.Summary.Counts.Total);

        // It is in the group's chat, not a separate one-off chat; the category chat stays empty.
        chats = (await client.GetFromJsonAsync<List<ChatListItemDto>>("/api/v1/chats", Ct))!;
        Assert.DoesNotContain(chats, c => c.Kind == "broadcast");
        Assert.Equal("Hi {{name}}", chats.Single(c => c.Id == friends.Id).LastText);
        Assert.Empty((await client.GetFromJsonAsync<TargetChatDto>($"/api/v1/chats/category/{family.Id}", Ct))!.Messages);

        // Taking Asha out of the category leaves her without one.
        var without = await PostAsync<CategoryDto>(client, $"/api/v1/categories/{family.Id}/members", new GroupMembersRequest([asha], false));
        Assert.Equal(0, without.ContactCount);
    }

    [Fact]
    public async Task Todays_birthdays_are_one_chat_with_who_got_wishes_and_who_did_not()
    {
        var (client, _) = await ConnectedOwnerAsync();
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));
        var kiran = await ContactAsync(client, "+919825053333", "Kiran", new BirthdayDto(today.Day, today.Month, 1990));
        await ContactAsync(client, "+919825054444", "Meena", new BirthdayDto(today.Day, today.Month, null));

        var before = (await client.GetFromJsonAsync<BirthdaysChatDto>("/api/v1/chats/birthdays", Ct))!;
        Assert.Equal(2, before.People.Count);
        Assert.All(before.People, p => Assert.Equal("not_sent", p.Status));
        Assert.Equal(today.Year - 1990, before.People.Single(p => p.ContactId == kiran).TurnsAge);

        var wish = new BroadcastInput("Birthday", null, null, null, null, null, [kiran], "Happy birthday {{name}}", ForBirthday: true);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/broadcasts", wish, Ct)).StatusCode);
        await RunSenderAsync();

        var after = (await client.GetFromJsonAsync<BirthdaysChatDto>("/api/v1/chats/birthdays", Ct))!;
        Assert.Equal("sent", after.People.Single(p => p.ContactId == kiran).Status);
        Assert.Equal("not_sent", after.People.Single(p => p.ContactId != kiran).Status);
        Assert.Single(after.Messages);

        var chats = (await client.GetFromJsonAsync<List<ChatListItemDto>>("/api/v1/chats", Ct))!;
        // Birthday wishes have their own view, so the Chats list stays clean.
        Assert.DoesNotContain(chats, c => c.Kind == "birthdays");
        Assert.DoesNotContain(chats, c => c.Kind == "broadcast");
    }

    [Fact]
    public async Task One_off_chats_leave_the_screen_five_days_after_they_finish()
    {
        var (client, userId) = await ConnectedOwnerAsync();
        var asha = await ContactAsync(client, "+919825055555", "Asha");
        var created = await client.PostAsJsonAsync(
            "/api/v1/broadcasts", new BroadcastInput("One-off", null, null, null, null, null, [asha], "Hi"), Ct);
        var summary = (await created.Content.ReadFromJsonAsync<BroadcastSummaryDto>(Ct))!;
        await RunSenderAsync();

        Assert.Contains((await client.GetFromJsonAsync<List<ChatListItemDto>>("/api/v1/chats", Ct))!, c => c.Id == summary.Id);

        await factory.WithDbAsync(db => db.Broadcasts.Where(b => b.Id == summary.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.FinishedAt, (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-6)), Ct));

        Assert.DoesNotContain((await client.GetFromJsonAsync<List<ChatListItemDto>>("/api/v1/chats", Ct))!, c => c.Id == summary.Id);
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<BroadcastSummaryDto>>("/api/v1/broadcasts", Ct))!, b => b.Id == summary.Id);

        // Hidden, not deleted: it can still be opened directly.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/broadcasts/{summary.Id}", Ct)).StatusCode);
        await factory.WithDbAsync(async db => Assert.True(await db.Broadcasts.AnyAsync(b => b.Id == summary.Id && b.UserId == userId, Ct)));
    }

    private async Task<int> RunSenderAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BroadcastDispatcher>().RunOnceAsync(Ct);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private static async Task<Guid> ContactAsync(HttpClient client, string phone, string name, BirthdayDto? birthday = null) =>
        (await PostAsync<ContactDto>(client, "/api/v1/contacts", new ContactInput(phone, name, name, null, null, "en", null, null, birthday, null, null))).Id;

    private async Task<(HttpClient Client, Guid UserId)> ConnectedOwnerAsync()
    {
        var userName = await factory.CreateUserAsync(SystemRoles.Owner);
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
            Content = JsonContent.Create(new { type = "connection", accountId, status = "connected", reason = "linked", phoneNumber = "+911111111111" }),
        };
        request.Headers.Add("X-Gateway-Secret", ApiFactory.GatewaySecret);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().SendAsync(request, Ct)).StatusCode);
        return (client, userId);
    }
}
