using System.Net;
using System.Net.Http.Json;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Tests.Infrastructure;
using WhatsAppAutomation.Application.Features.Templates;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Tests.Templates;

public sealed class TemplatesApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_saves_edits_and_deletes_their_own_templates_and_nobody_else_sees_them()
    {
        var client = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));

        var created = await client.PostAsJsonAsync("/api/v1/templates", new SaveTemplateRequest("Diwali", "Happy Diwali {{name}} 🪔"), Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var diwali = (await created.Content.ReadFromJsonAsync<TemplateDto>(Ct))!;

        var duplicate = await client.PostAsJsonAsync("/api/v1/templates", new SaveTemplateRequest(" diwali ", "Other"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var edited = await client.PutAsJsonAsync($"/api/v1/templates/{diwali.Id}", new SaveTemplateRequest("Diwali", "Shubh Deepavali {{name_gu}}"), Ct);
        Assert.Equal("Shubh Deepavali {{name_gu}}", (await edited.Content.ReadFromJsonAsync<TemplateDto>(Ct))!.Text);

        var other = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        Assert.Empty((await other.GetFromJsonAsync<List<TemplateDto>>("/api/v1/templates", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/templates/{diwali.Id}", Ct)).StatusCode);

        Assert.Single((await client.GetFromJsonAsync<List<TemplateDto>>("/api/v1/templates", Ct))!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/templates/{diwali.Id}", Ct)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<TemplateDto>>("/api/v1/templates", Ct))!);
    }

    [Fact]
    public async Task Every_member_reads_templates_but_only_the_templates_switch_lets_them_save_one()
    {
        var member = await factory.CreateClientForAsync(await factory.CreateUserAsync());
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/templates", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsJsonAsync("/api/v1/templates", new SaveTemplateRequest("Hi", "Hi {{name}}"), Ct)).StatusCode);

        var allowed = await factory.CreateClientForAsync(
            await factory.CreateUserAsync(switches: [Permissions.Templates.Manage]));
        Assert.Equal(HttpStatusCode.OK,
            (await allowed.PostAsJsonAsync("/api/v1/templates", new SaveTemplateRequest("Hi", "Hi {{name}}"), Ct)).StatusCode);
    }
}
