using System.Net;
using System.Net.Http.Json;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Tests.Infrastructure;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Tests.Access;

public sealed class AccessControlTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Owner_has_every_permission()
    {
        var owner = await factory.CreateClientForAsync(await factory.CreateUserAsync(SystemRoles.Owner));

        var me = await owner.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Ct);

        Assert.Equal(Permissions.All.Select(p => p.Name).Order(), me!.Permissions.Order());
    }

    [Fact]
    public async Task User_without_the_permission_gets_403_and_anonymous_gets_401()
    {
        var sender = await factory.CreateClientForAsync(await factory.CreateUserAsync(await NewRoleAsync()));

        Assert.Equal(HttpStatusCode.Forbidden, (await sender.GetAsync("/api/v1/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/users", Ct)).StatusCode);
    }

    [Fact]
    public async Task Granting_a_permission_to_a_role_takes_effect_at_once()
    {
        var owner = await OwnerAsync();
        var roleName = await NewRoleAsync();
        var member = await factory.CreateClientForAsync(await factory.CreateUserAsync(roleName));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/users", Ct)).StatusCode);

        var roleId = await factory.RoleIdAsync(roleName);
        var update = await owner.PutAsJsonAsync(
            $"/api/v1/roles/{roleId}", new SaveRoleRequest(roleName, null, [Permissions.Users.View]), Ct);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/roles", Ct)).StatusCode);
    }

    [Fact]
    public async Task Owner_can_create_read_and_delete_a_role()
    {
        var owner = await OwnerAsync();
        var name = "Helpers " + Guid.NewGuid().ToString("N")[..6];

        var created = await owner.PostAsJsonAsync(
            "/api/v1/roles", new SaveRoleRequest(name, "Can see users", [Permissions.Users.View]), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedResponse>(Ct))!.Id;

        var role = await owner.GetFromJsonAsync<RoleResponse>($"/api/v1/roles/{id}", Ct);
        Assert.Equal(name, role!.Name);
        Assert.Equal([Permissions.Users.View], role.Permissions);
        Assert.False(role.IsSystem);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/roles/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/roles/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Role_names_are_unique_without_case_and_Owner_is_reserved()
    {
        var owner = await OwnerAsync();
        var name = await NewRoleAsync();

        var duplicate = await owner.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest(name.ToUpperInvariant(), null, []), Ct);
        var reserved = await owner.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("owner", null, []), Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reserved.StatusCode);
    }

    [Fact]
    public async Task Unknown_permissions_are_rejected()
    {
        var owner = await OwnerAsync();

        var response = await owner.PostAsJsonAsync(
            "/api/v1/roles", new SaveRoleRequest("Bad " + Guid.NewGuid().ToString("N")[..6], null, ["everything.allowed"]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Owner_role_cannot_be_changed_or_deleted()
    {
        var owner = await OwnerAsync();
        var ownerRoleId = await factory.RoleIdAsync(SystemRoles.Owner);

        var update = await owner.PutAsJsonAsync($"/api/v1/roles/{ownerRoleId}", new SaveRoleRequest("Boss", null, []), Ct);
        var delete = await owner.DeleteAsync($"/api/v1/roles/{ownerRoleId}", Ct);

        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task A_role_that_users_still_have_cannot_be_deleted()
    {
        var owner = await OwnerAsync();
        var roleName = await NewRoleAsync();
        await factory.CreateUserAsync(roleName);

        var response = await owner.DeleteAsync($"/api/v1/roles/{await factory.RoleIdAsync(roleName)}", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Owner_can_create_a_user_who_can_then_sign_in()
    {
        var owner = await OwnerAsync();
        var userName = "new_" + Guid.NewGuid().ToString("N")[..8];
        var senderRoleId = await factory.RoleIdAsync(SystemRoles.Sender);

        var created = await owner.PostAsJsonAsync(
            "/api/v1/users", new CreateUserRequest(userName, "New Person", ApiFactory.TestPassword, [senderRoleId]), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var client = await factory.CreateClientForAsync(userName);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Ct);
        Assert.Equal([SystemRoles.Sender], me!.User.Roles);
    }

    [Fact]
    public async Task Creating_a_user_with_a_short_password_or_taken_name_fails()
    {
        var owner = await OwnerAsync();
        var existing = await factory.CreateUserAsync();
        var senderRoleId = await factory.RoleIdAsync(SystemRoles.Sender);

        var shortPassword = await owner.PostAsJsonAsync(
            "/api/v1/users", new CreateUserRequest("u_" + Guid.NewGuid().ToString("N")[..8], "X", "short", [senderRoleId]), Ct);
        var taken = await owner.PostAsJsonAsync(
            "/api/v1/users", new CreateUserRequest(existing, "X", ApiFactory.TestPassword, [senderRoleId]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
    }

    [Fact]
    public async Task A_user_may_have_a_short_password_but_not_an_obvious_one_and_account_managers_need_twelve_characters()
    {
        var owner = await OwnerAsync();
        var senderRoleId = await factory.RoleIdAsync(SystemRoles.Sender);
        var ownerRoleId = await factory.RoleIdAsync(SystemRoles.Owner);
        Task<HttpResponseMessage> CreateAsync(string password, Guid roleId) => owner.PostAsJsonAsync(
            "/api/v1/users", new CreateUserRequest("u_" + Guid.NewGuid().ToString("N")[..8], "X", password, [roleId]), Ct);

        Assert.True((await CreateAsync("riya42", senderRoleId)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync("123456", senderRoleId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync("riya4242", ownerRoleId)).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_user_signs_them_out_and_blocks_their_permissions()
    {
        var owner = await OwnerAsync();
        var roleName = await NewRoleAsync(Permissions.Users.View);
        var userName = await factory.CreateUserAsync(roleName);
        var member = await factory.CreateClientForAsync(userName);
        var userId = (await member.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Ct))!.User.Id;

        var disable = await owner.PutAsJsonAsync($"/api/v1/users/{userId}/active", new SetUserActiveRequest(false), Ct);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/users", Ct)).StatusCode);
        var login = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(userName, ApiFactory.TestPassword, "d", null), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
    }

    [Fact]
    public async Task You_cannot_disable_yourself()
    {
        var owner = await OwnerAsync();
        var me = await owner.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Ct);

        var response = await owner.PutAsJsonAsync($"/api/v1/users/{me!.User.Id}/active", new SetUserActiveRequest(false), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Changing_a_users_roles_takes_effect_at_once()
    {
        var owner = await OwnerAsync();
        var userName = await factory.CreateUserAsync(await NewRoleAsync());
        var member = await factory.CreateClientForAsync(userName);
        var userId = (await member.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", Ct))!.User.Id;
        var viewerRoleId = await factory.RoleIdAsync(await NewRoleAsync(Permissions.Roles.View));

        var update = await owner.PutAsJsonAsync($"/api/v1/users/{userId}/roles", new SetUserRolesRequest([viewerRoleId]), Ct);

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/roles", Ct)).StatusCode);
    }

    [Fact]
    public async Task Users_list_shows_each_users_roles()
    {
        var owner = await OwnerAsync();
        var userName = await factory.CreateUserAsync(SystemRoles.Sender);

        var users = await owner.GetFromJsonAsync<List<UserDetailsResponse>>("/api/v1/users", Ct);

        var user = Assert.Single(users!, u => u.UserName == userName);
        Assert.Equal([SystemRoles.Sender], user.Roles.Select(r => r.Name));
    }

    [Fact]
    public async Task Permissions_list_matches_the_code()
    {
        var owner = await OwnerAsync();

        var permissions = await owner.GetFromJsonAsync<List<PermissionResponse>>("/api/v1/permissions", Ct);

        Assert.Equal(Permissions.All.Select(p => p.Name).Order(), permissions!.Select(p => p.Name).Order());
    }

    private async Task<HttpClient> OwnerAsync() =>
        await factory.CreateClientForAsync(await factory.CreateUserAsync(SystemRoles.Owner));

    /// <summary>Creates a role with a unique name through the API and returns the name.</summary>
    private async Task<string> NewRoleAsync(params string[] permissions)
    {
        var owner = await OwnerAsync();
        var name = "Role " + Guid.NewGuid().ToString("N")[..8];
        var response = await owner.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest(name, null, permissions), Ct);
        response.EnsureSuccessStatusCode();
        return name;
    }
}
