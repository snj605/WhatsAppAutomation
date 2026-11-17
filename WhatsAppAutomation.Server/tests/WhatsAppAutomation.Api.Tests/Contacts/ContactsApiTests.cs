using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Tests.Infrastructure;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Application.Features.Contacts.Import;
using WhatsAppAutomation.Application.Features.Contacts.Queries;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Tests.Contacts;

public sealed class ContactsApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_contact_is_saved_with_a_normalized_number_its_category_and_groups()
    {
        var client = await OwnerAsync();
        var elders = await CreateCategoryAsync(client, "Elders", "#10B981");
        var family = await CreateGroupAsync(client, "Family – Surat");

        var response = await client.PostAsJsonAsync("/api/v1/contacts", Input("+91 98765-43210", elders.Id, "gu", nameGu: "રમેશભાઈ", groups: [family.Id]), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contact = (await response.Content.ReadFromJsonAsync<ContactDto>(Ct))!;
        Assert.Equal("+919876543210", contact.PhoneNumber);
        Assert.Equal("gu", contact.Language);
        Assert.Equal("રમેશભાઈ", contact.NameGu);
        Assert.Equal(new BirthdayDto(15, 8, null), contact.Birthday);
        Assert.Equal("Family – Surat", Assert.Single(contact.Groups).Name);
        Assert.False(contact.NeedsDetails);

        var categories = await client.GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories", Ct);
        Assert.Equal(1, Assert.Single(categories!).ContactCount);
        Assert.Equal("#10b981", categories![0].Color);
        var groups = await client.GetFromJsonAsync<List<GroupDto>>("/api/v1/groups", Ct);
        Assert.Equal(1, Assert.Single(groups!).ContactCount);
    }

    [Fact]
    public async Task A_contact_needs_the_real_name_in_its_language_a_unique_number_and_a_real_birthday()
    {
        var client = await OwnerAsync();
        var category = await CreateCategoryAsync(client, "Others", "#64748b");

        var noName = await client.PostAsJsonAsync("/api/v1/contacts", Input("+919876543210", category.Id, "hi", nameEn: "Ramesh"), Ct);
        await AssertProblemAsync(noName, HttpStatusCode.BadRequest, "contacts.missing_name");

        var noCountryCode = await client.PostAsJsonAsync("/api/v1/contacts", Input("98765 43210", category.Id, "en", nameEn: "Ramesh"), Ct);
        await AssertProblemAsync(noCountryCode, HttpStatusCode.BadRequest, "contacts.invalid_phone");

        var badBirthday = Input("+919876543210", category.Id, "en", nameEn: "Ramesh") with { Birthday = new BirthdayDto(31, 2, null) };
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/v1/contacts", badBirthday, Ct), HttpStatusCode.BadRequest, "contacts.invalid_birthday");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/contacts", Input("+919876543210", category.Id, "en", nameEn: "Ramesh"), Ct)).StatusCode);
        var duplicate = await client.PostAsJsonAsync("/api/v1/contacts", Input("+91 98765 43210", category.Id, "en", nameEn: "Other"), Ct);
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "contacts.duplicate_phone");

        // Deleting a category keeps its contacts; they just have no category any more.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/categories/{category.Id}", Ct)).StatusCode);
        var page = (await client.GetFromJsonAsync<ContactPageDto>("/api/v1/contacts", Ct))!;
        Assert.Null(Assert.Single(page.Items).CategoryId);
    }

    [Fact]
    public async Task Users_see_and_change_only_their_own_contacts()
    {
        var first = await OwnerAsync();
        var second = await OwnerAsync();
        var category = await CreateCategoryAsync(first, "Elders", "#10b981");
        var contact = (await (await first.PostAsJsonAsync("/api/v1/contacts", Input("+919876543210", category.Id, "en", nameEn: "Ramesh"), Ct))
            .Content.ReadFromJsonAsync<ContactDto>(Ct))!;

        var page = await second.GetFromJsonAsync<ContactPageDto>("/api/v1/contacts", Ct);
        Assert.Equal(0, page!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"/api/v1/contacts/{contact.Id}", Ct)).StatusCode);

        // Someone else's category cannot be used.
        var otherCategory = await second.PostAsJsonAsync("/api/v1/contacts", Input("+919876543210", category.Id, "en", nameEn: "Ramesh"), Ct);
        await AssertProblemAsync(otherCategory, HttpStatusCode.NotFound, "categories.not_found");

        var deleted = await second.PostAsJsonAsync("/api/v1/contacts/delete", new DeleteContactsRequest([contact.Id]), Ct);
        Assert.Equal(0, (await deleted.Content.ReadFromJsonAsync<DeletedResponse>(Ct))!.Deleted);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync($"/api/v1/contacts/{contact.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Search_and_filters_find_contacts_by_number_any_name_category_group_and_missing_details()
    {
        var client = await OwnerAsync();
        var elders = await CreateCategoryAsync(client, "Elders", "#10b981");
        var youngers = await CreateCategoryAsync(client, "Youngers", "#3b82f6");
        var group = await CreateGroupAsync(client, "Satsang");
        await client.PostAsJsonAsync("/api/v1/contacts", Input("+919876543210", elders.Id, "gu", nameGu: "રમેશભાઈ", savedName: "Daddy", groups: [group.Id]), Ct);
        await client.PostAsJsonAsync("/api/v1/contacts", Input("+919812345678", youngers.Id, "en", nameEn: "Rahul"), Ct);

        Assert.Equal(1, (await PageAsync(client, "?search=daddy")).Total);
        Assert.Equal(1, (await PageAsync(client, "?search=રમેશ")).Total);
        Assert.Equal(1, (await PageAsync(client, "?search=98123")).Total);
        Assert.Equal(1, (await PageAsync(client, $"?categoryId={youngers.Id}")).Total);
        Assert.Equal("Daddy", (await PageAsync(client, $"?groupId={group.Id}")).Items.Single().SavedName);
        Assert.Equal(0, (await PageAsync(client, "?needsDetails=true")).Total);
        Assert.Equal(0, (await PageAsync(client, "?search=%25")).Total);
    }

    [Fact]
    public async Task A_CSV_import_previews_without_saving_then_creates_and_updates_by_phone()
    {
        var client = await OwnerAsync();
        var elders = await CreateCategoryAsync(client, "Elders", "#10b981");
        await client.PostAsJsonAsync("/api/v1/contacts", Input("+919876543210", elders.Id, "en", nameEn: "Ramesh", savedName: "Daddy"), Ct);

        const string csv = """
            Phone*,Saved name,Name (English),Name (Hindi),Name (Gujarati),Category*,Language,Salutation,Birthday,Groups,Notes
            +91 98765 43210,,,,રમેશભાઈ,Elders,gu,Pujya,15-08-1960,"Family, Surat",
            +919812345678,Rahul,Rahul,राहुल,,Youngers,hi,,02-10,Family,
            12345,Bad,Bad,,,Others,en,,,,
            +919812345678,Again,Again,,,Youngers,en,,,,
            +447700900123,Mr Smith,,,,Others,en,,,,
            """;

        var preview = await ImportAsync(client, csv, "contacts.csv", apply: false);
        Assert.False(preview.Applied);
        Assert.Equal(1, preview.Created);
        Assert.Equal(1, preview.Updated);
        Assert.Equal(3, preview.Failed);
        Assert.Equal(["Youngers"], preview.NewCategories);
        Assert.Equal(["Family", "Surat"], preview.NewGroups);
        Assert.Contains(preview.Rows.Single(r => r.Row == 4).Errors, e => e.StartsWith("Phone:", StringComparison.Ordinal));
        Assert.Contains(preview.Rows.Single(r => r.Row == 5).Errors, e => e.Contains("row 3", StringComparison.Ordinal));
        Assert.Contains(preview.Rows.Single(r => r.Row == 6).Errors, e => e.StartsWith("Name (English)", StringComparison.Ordinal));
        Assert.Equal(1, (await PageAsync(client, string.Empty)).Total);

        var applied = await ImportAsync(client, csv, "contacts.csv", apply: true);
        Assert.True(applied.Applied);
        var contacts = (await PageAsync(client, string.Empty)).Items;
        Assert.Equal(2, contacts.Count);
        var daddy = contacts.Single(c => c.PhoneNumber == "+919876543210");
        Assert.Equal("Daddy", daddy.SavedName);
        Assert.Equal("Ramesh", daddy.NameEn);
        Assert.Equal("રમેશભાઈ", daddy.NameGu);
        Assert.Equal("gu", daddy.Language);
        Assert.Equal(new BirthdayDto(15, 8, 1960), daddy.Birthday);
        Assert.Equal(["Family", "Surat"], daddy.Groups.Select(g => g.Name));
        var rahul = contacts.Single(c => c.PhoneNumber == "+919812345678");
        Assert.Equal(new BirthdayDto(2, 10, null), rahul.Birthday);
        Assert.Equal("राहुल", rahul.NameHi);
    }

    [Fact]
    public async Task Import_refuses_files_it_cannot_read_safely()
    {
        var client = await OwnerAsync();

        var latin1 = await ImportRawAsync(client, Encoding.Latin1.GetBytes("Phone,Category\n+919876543210,Ältere\n"), "c.csv");
        await AssertProblemAsync(latin1, HttpStatusCode.BadRequest, "validation_failed");
        // Phone is the only required column (Category is optional).
        var noPhoneColumn = await ImportRawAsync(client, Encoding.UTF8.GetBytes("Name (English)\nRiya\n"), "c.csv");
        await AssertProblemAsync(noPhoneColumn, HttpStatusCode.BadRequest, "validation_failed");
        var onlyPhone = await ImportRawAsync(client, Encoding.UTF8.GetBytes("Phone\n+919876543210\n"), "c.csv");
        Assert.Equal(HttpStatusCode.OK, onlyPhone.StatusCode);
        var wrongType = await ImportRawAsync(client, [1, 2, 3], "c.txt");
        await AssertProblemAsync(wrongType, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task The_template_downloads_and_imports_its_own_sample_rows()
    {
        var client = await OwnerAsync();

        var template = await client.GetAsync("/api/v1/contacts/import/template", Ct);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        var bytes = await template.Content.ReadAsByteArrayAsync(Ct);

        var response = await ImportRawAsync(client, bytes, "contacts-template.xlsx");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<ImportResultDto>(Ct))!;
        Assert.Equal(3, preview.Created);
        Assert.Equal(0, preview.Failed);
        Assert.Equal(["Elders", "Youngers", "Others"], preview.NewCategories);
    }

    [Fact]
    public async Task Contacts_need_their_permissions_and_preferences_default_to_English()
    {
        // The platform admin never reaches anyone's contacts.
        var platform = await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.PlatformAdmin));
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/v1/contacts", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/categories", Ct)).StatusCode);

        var client = await OwnerAsync();
        Assert.Equal("en", (await client.GetFromJsonAsync<PreferencesDto>("/api/v1/preferences", Ct))!.DefaultLanguage);
        var saved = await client.PutAsJsonAsync("/api/v1/preferences", new SetPreferencesRequest("gu"), Ct);
        Assert.Equal("gu", (await saved.Content.ReadFromJsonAsync<PreferencesDto>(Ct))!.DefaultLanguage);
        Assert.Equal("gu", (await client.GetFromJsonAsync<PreferencesDto>("/api/v1/preferences", Ct))!.DefaultLanguage);
    }

    private static ContactInput Input(
        string phone, Guid categoryId, string language, string? nameEn = null, string? nameGu = null, string? savedName = null,
        IReadOnlyList<Guid>? groups = null) =>
        new(phone, savedName, nameEn, null, nameGu, language, categoryId, null, new BirthdayDto(15, 8, null), null, groups);

    private async Task<HttpClient> OwnerAsync() => await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));

    private static async Task<CategoryDto> CreateCategoryAsync(HttpClient client, string name, string color)
    {
        var response = await client.PostAsJsonAsync("/api/v1/categories", new SaveCategoryRequest(name, color), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(Ct))!;
    }

    private static async Task<GroupDto> CreateGroupAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/groups", new SaveGroupRequest(name), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GroupDto>(Ct))!;
    }

    private static async Task<ContactPageDto> PageAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<ContactPageDto>("/api/v1/contacts" + query, Ct))!;

    private static async Task<ImportResultDto> ImportAsync(HttpClient client, string csv, string fileName, bool apply)
    {
        var response = await ImportRawAsync(client, Encoding.UTF8.GetBytes(csv), fileName, apply);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportResultDto>(Ct))!;
    }

    private static async Task<HttpResponseMessage> ImportRawAsync(HttpClient client, byte[] bytes, string fileName, bool apply = false)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return await client.PostAsync($"/api/v1/contacts/import?apply={apply.ToString().ToLowerInvariant()}", form, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains(code, body, StringComparison.Ordinal);
    }
}
