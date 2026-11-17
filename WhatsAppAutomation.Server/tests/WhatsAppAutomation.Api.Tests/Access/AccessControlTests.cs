using System.Net;
using System.Net.Http.Json;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Tests.Infrastructure;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Tests.Access;

/// <summary>Platform admin, tenant admins and members: who may do what, and that tenants stay apart.</summary>
public sealed class AccessControlTests(ApiFactory factory)
{
    private const string AdminPassword = "a-long-admin-password-42";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_kind_of_user_gets_its_own_permissions()
    {
        var platform = await MeAsync(await factory.CreateUserAsync(UserKind.PlatformAdmin));
        var admin = await MeAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        var member = await MeAsync(await factory.CreateUserAsync(switches: [Permissions.Broadcasts.Send]));

        Assert.Equal([Permissions.Platform.Tenants], platform.Permissions);
        Assert.Equal(UserKinds.TenantAdmin.Order(StringComparer.Ordinal), admin.Permissions);
        Assert.Equal(
            UserKinds.MemberBase.Append(Permissions.Broadcasts.Send).Order(StringComparer.Ordinal), member.Permissions);
        Assert.Equal(UserKinds.PlatformAdminCode, platform.User.Kind);
        Assert.Null(platform.User.TenantName);
    }

    [Fact]
    public async Task Anonymous_gets_401_and_a_member_gets_403_on_admin_screens()
    {
        var member = await factory.CreateClientForAsync(await factory.CreateUserAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/members", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/members", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/platform/tenants", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_tenant_admin_adds_a_member_who_must_choose_their_own_password_first()
    {
        var admin = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        var userName = "m_" + Guid.NewGuid().ToString("N")[..8];

        var created = await admin.PostAsJsonAsync(
            "/api/v1/members", new CreateMemberRequest(userName, "New Person", "riya42", false, [Permissions.Contacts.Import]), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var member = await SignInAsync(userName, "riya42");
        var me = (await member.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Ct))!;
        Assert.True(me.User.MustChangePassword);
        Assert.Empty(me.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/contacts", Ct)).StatusCode);

        var changed = await member.PutAsJsonAsync("/api/v1/auth/password", new ChangePasswordRequest("riya42", "om1978"), Ct);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        me = (await member.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Ct))!;
        Assert.False(me.User.MustChangePassword);
        Assert.Contains(Permissions.Contacts.Import, me.Permissions);
        Assert.DoesNotContain(Permissions.Broadcasts.Send, me.Permissions);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/contacts", Ct)).StatusCode);
    }

    [Fact]
    public async Task Changing_a_members_switches_takes_effect_at_once()
    {
        var adminName = await factory.CreateUserAsync(UserKind.TenantAdmin);
        var admin = await factory.CreateClientForAsync(adminName);
        var memberName = await factory.CreateUserAsync(tenantId: await factory.TenantIdAsync(adminName));
        var member = await factory.CreateClientForAsync(memberName);
        var memberId = await factory.UserIdAsync(memberName);
        var save = new SaveTemplateRequest("Hi", "Hi {{name}}");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/v1/templates", save, Ct)).StatusCode);

        var update = await admin.PutAsJsonAsync(
            $"/api/v1/members/{memberId}", new UpdateMemberRequest("Renamed", false, [Permissions.Templates.Manage]), Ct);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await member.PostAsJsonAsync("/api/v1/templates", save, Ct)).StatusCode);
        var listed = Assert.Single((await admin.GetFromJsonAsync<List<MemberResponse>>("/api/v1/members", Ct))!, m => m.Id == memberId);
        Assert.Equal("Renamed", listed.DisplayName);
        Assert.Equal([Permissions.Templates.Manage], listed.Switches);
    }

    [Fact]
    public async Task Unknown_switches_are_rejected()
    {
        var admin = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));

        var response = await admin.PostAsJsonAsync(
            "/api/v1/members", new CreateMemberRequest("u_" + Guid.NewGuid().ToString("N")[..8], "X", "riya42", false, [Permissions.Members.Manage]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Members_may_have_a_short_password_but_admins_need_twelve_characters_and_names_are_unique()
    {
        var admin = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        var existing = await factory.CreateUserAsync();
        Task<HttpResponseMessage> CreateAsync(string password, bool isAdmin, string? name = null) => admin.PostAsJsonAsync(
            "/api/v1/members", new CreateMemberRequest(name ?? "u_" + Guid.NewGuid().ToString("N")[..8], "X", password, isAdmin, []), Ct);

        Assert.True((await CreateAsync("riya42", false)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync("123456", false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync("riya4242", true)).StatusCode);
        Assert.True((await CreateAsync(AdminPassword, true)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CreateAsync("riya42", false, existing)).StatusCode);
    }

    [Fact]
    public async Task Tenants_stay_apart()
    {
        var adminA = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));
        var memberB = await factory.CreateUserAsync();
        var memberBId = await factory.UserIdAsync(memberB);

        var listed = await adminA.GetFromJsonAsync<List<MemberResponse>>("/api/v1/members", Ct);
        Assert.DoesNotContain(listed!, m => m.Id == memberBId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await adminA.PutAsJsonAsync($"/api/v1/members/{memberBId}/active", new SetActiveRequest(false), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminA.DeleteAsync($"/api/v1/members/{memberBId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await adminA.PutAsJsonAsync($"/api/v1/members/{memberBId}/password", new SetPasswordRequest("riya42"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Turning_a_member_off_signs_them_out_and_blocks_them()
    {
        var adminName = await factory.CreateUserAsync(UserKind.TenantAdmin);
        var admin = await factory.CreateClientForAsync(adminName);
        var memberName = await factory.CreateUserAsync(tenantId: await factory.TenantIdAsync(adminName));
        var member = await factory.CreateClientForAsync(memberName);

        var off = await admin.PutAsJsonAsync(
            $"/api/v1/members/{await factory.UserIdAsync(memberName)}/active", new SetActiveRequest(false), Ct);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/contacts", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await LoginAsync(memberName, ApiFactory.TestPassword)).StatusCode);
    }

    [Fact]
    public async Task You_cannot_change_yourself_and_the_last_admin_stays()
    {
        var adminName = await factory.CreateUserAsync(UserKind.TenantAdmin);
        var admin = await factory.CreateClientForAsync(adminName);
        var adminId = await factory.UserIdAsync(adminName);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync($"/api/v1/members/{adminId}/active", new SetActiveRequest(false), Ct)).StatusCode);

        // With two active admins, one may turn the other off.
        var tenantId = (await factory.TenantIdAsync(adminName))!.Value;
        var secondName = await factory.CreateUserAsync(UserKind.TenantAdmin, tenantId: tenantId);
        var second = await factory.CreateClientForAsync(secondName);
        var offFirst = await second.PutAsJsonAsync($"/api/v1/members/{adminId}/active", new SetActiveRequest(false), Ct);
        Assert.Equal(HttpStatusCode.NoContent, offFirst.StatusCode);

        // Now the second is the last active admin, so not even the platform admin can turn them off.
        var platform = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.PlatformAdmin));
        var offSecond = await platform.PutAsJsonAsync(
            $"/api/v1/platform/tenants/{tenantId}/admins/{await factory.UserIdAsync(secondName)}/active", new SetActiveRequest(false), Ct);
        Assert.Equal(HttpStatusCode.Conflict, offSecond.StatusCode);
    }

    [Fact]
    public async Task The_platform_admin_creates_a_tenant_with_its_first_admin_and_sees_counts_only()
    {
        var platform = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.PlatformAdmin));
        var name = "Shree " + Guid.NewGuid().ToString("N")[..6];
        var adminName = "a_" + Guid.NewGuid().ToString("N")[..8];

        var created = await platform.PostAsJsonAsync(
            "/api/v1/platform/tenants", new CreateTenantRequest(name, adminName, "First Admin", AdminPassword), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tenantId = (await created.Content.ReadFromJsonAsync<CreatedResponse>(Ct))!.Id;

        var duplicate = await platform.PostAsJsonAsync(
            "/api/v1/platform/tenants", new CreateTenantRequest(name.ToUpperInvariant(), "b_" + Guid.NewGuid().ToString("N")[..8], "X", AdminPassword), Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var tenant = Assert.Single((await platform.GetFromJsonAsync<List<TenantResponse>>("/api/v1/platform/tenants", Ct))!, t => t.Id == tenantId);
        Assert.Equal((1, 1, true), (tenant.Users, tenant.Admins, tenant.IsActive));

        var admins = await platform.GetFromJsonAsync<List<TenantAdminResponse>>($"/api/v1/platform/tenants/{tenantId}/admins", Ct);
        Assert.Equal(adminName, Assert.Single(admins!).UserName);

        var admin = await SignInAsync(adminName, AdminPassword);
        var me = (await admin.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Ct))!;
        Assert.Equal((name, true), (me.User.TenantName, me.User.MustChangePassword));

        // The platform admin reaches no tenant's contacts or members.
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/v1/members", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/v1/contacts", Ct)).StatusCode);
    }

    [Fact]
    public async Task Turning_a_tenant_off_blocks_everyone_in_it_and_only_a_turned_off_tenant_can_be_deleted()
    {
        var platform = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.PlatformAdmin));
        var adminName = await factory.CreateUserAsync(UserKind.TenantAdmin);
        var admin = await factory.CreateClientForAsync(adminName);
        var tenantId = (await factory.TenantIdAsync(adminName))!.Value;

        Assert.Equal(HttpStatusCode.Conflict, (await platform.DeleteAsync($"/api/v1/platform/tenants/{tenantId}", Ct)).StatusCode);

        var off = await platform.PutAsJsonAsync($"/api/v1/platform/tenants/{tenantId}/active", new SetActiveRequest(false), Ct);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/members", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await LoginAsync(adminName, ApiFactory.TestPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await platform.DeleteAsync($"/api/v1/platform/tenants/{tenantId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(adminName, ApiFactory.TestPassword)).StatusCode);
    }

    [Fact]
    public async Task A_reset_password_signs_the_member_out_and_must_be_changed()
    {
        var adminName = await factory.CreateUserAsync(UserKind.TenantAdmin);
        var admin = await factory.CreateClientForAsync(adminName);
        var memberName = await factory.CreateUserAsync(tenantId: await factory.TenantIdAsync(adminName));

        var reset = await admin.PutAsJsonAsync(
            $"/api/v1/members/{await factory.UserIdAsync(memberName)}/password", new SetPasswordRequest("temp42"), Ct);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(memberName, ApiFactory.TestPassword)).StatusCode);
        var member = await SignInAsync(memberName, "temp42");
        Assert.True((await member.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Ct))!.User.MustChangePassword);
    }

    private async Task<MeResponse> MeAsync(string userName) =>
        (await (await factory.CreateClientForAsync(userName)).GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Ct))!;

    private Task<HttpResponseMessage> LoginAsync(string userName, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, password, "d", null), Ct);

    private async Task<HttpClient> SignInAsync(string userName, string password)
    {
        var response = await LoginAsync(userName, password);
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(Ct))!;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        return client;
    }
}
