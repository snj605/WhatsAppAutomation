using System.Net;
using System.Net.Http.Json;
using System.Text;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Tests.Infrastructure;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.ContactFields;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Application.Features.Contacts.Import;
using WhatsAppAutomation.Application.Features.Modules;
using WhatsAppAutomation.Application.Features.Salutations;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Tests.Contacts;

/// <summary>A contact's occasion dates and the user's own custom fields: saving, checking, messages, search and Excel.</summary>
public sealed class ContactFieldsApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_contact_keeps_only_the_occasions_it_is_given_and_a_birthday_survives_deleting_the_birthday_occasion()
    {
        var client = await ClientAsync();
        var birthday = await ModuleAsync(client, "Birthday", "birthday");
        var asthi = await ModuleAsync(client, "Asthi", "yearly", "In memory of");
        var opening = await ModuleAsync(client, "Opening", "once");

        var created = await SaveAsync(client, null, Input(ApiFactory.UniquePhone(), "Ramesh") with
        {
            Occasions = [new(birthday.Id, 15, 8, 1960, "ignored"), new(asthi.Id, 5, 3, 2019, "Shantaben")],
        });
        Assert.Equal(2, created.Occasions.Count);
        Assert.Null(created.Occasions.Single(o => o.ModuleId == birthday.Id).ForName);
        Assert.Equal("Shantaben", created.Occasions.Single(o => o.ModuleId == asthi.Id).ForName);

        // Null leaves the dates; a list replaces them all.
        var renamed = await SaveAsync(client, created.Id, Input(created.PhoneNumber, "Rameshbhai"));
        Assert.Equal(2, renamed.Occasions.Count);
        var replaced = await SaveAsync(client, created.Id, Input(created.PhoneNumber, "Rameshbhai") with
        {
            Occasions = [new(birthday.Id, 15, 8, 1960, null)],
        });
        Assert.Equal(birthday.Id, Assert.Single(replaced.Occasions).ModuleId);

        // Checks: a one-time date needs a year, the date must exist, each occasion once, only own occasions.
        var phone = created.PhoneNumber;
        await AssertBadAsync(client, created.Id, Input(phone, "R") with { Occasions = [new(opening.Id, 1, 1, null, null)] }, HttpStatusCode.BadRequest);
        await AssertBadAsync(client, created.Id, Input(phone, "R") with { Occasions = [new(asthi.Id, 30, 2, null, null)] }, HttpStatusCode.BadRequest);
        await AssertBadAsync(client, created.Id, Input(phone, "R") with
        {
            Occasions = [new(asthi.Id, 1, 1, null, null), new(asthi.Id, 2, 2, null, null)],
        }, HttpStatusCode.BadRequest);
        var other = await ClientAsync();
        var foreign = await ModuleAsync(other, "Anniversary", "yearly");
        await AssertBadAsync(client, created.Id, Input(phone, "R") with { Occasions = [new(foreign.Id, 1, 1, null, null)] }, HttpStatusCode.NotFound);

        // Deleting the Birthday occasion keeps the birthday on the contact; a new Birthday occasion starts with it.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/modules/{birthday.Id}", Ct)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{created.Id}", Ct))!.Occasions);
        var again = await ModuleAsync(client, "Birthday", "birthday");
        var back = (await client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{created.Id}", Ct))!;
        Assert.Equal(new ContactOccasionDto(again.Id, 15, 8, 1960, null), Assert.Single(back.Occasions));
    }

    [Fact]
    public async Task Users_make_their_own_typed_fields_and_contacts_get_checked_values()
    {
        var client = await ClientAsync();
        var city = await FieldAsync(client, "City", "text");
        Assert.Equal("city", city.Key);
        var village = await FieldAsync(client, "Native village", "text");
        Assert.Equal("native_village", village.Key);
        var members = await FieldAsync(client, "Family members", "number");
        var joined = await FieldAsync(client, "Joined on", "date");
        var relation = await FieldAsync(client, "Relation", "choice", ["Uncle", "Aunt", "Friend"]);

        Assert.Equal(HttpStatusCode.Conflict, (await PostFieldAsync(client, " city ", "text")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostFieldAsync(client, "Salutation", "text")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostFieldAsync(client, "ગામ", "text")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostFieldAsync(client, "Colour", "color")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostFieldAsync(client, "Size", "choice", ["Only one"])).StatusCode);

        var contact = await SaveAsync(client, null, Input(ApiFactory.UniquePhone(), "Ramesh") with
        {
            Fields = new Dictionary<Guid, string?>
            {
                [city.Id] = " Rajkot ", [members.Id] = "1,200", [joined.Id] = "05-03-2019", [relation.Id] = "uncle",
            },
        });
        Assert.Equal("Rajkot", contact.Fields[city.Id]);
        Assert.Equal("1200", contact.Fields[members.Id]);
        Assert.Equal("2019-03-05", contact.Fields[joined.Id]);
        Assert.Equal("Uncle", contact.Fields[relation.Id]);

        await AssertBadAsync(client, contact.Id, Input(contact.PhoneNumber, "Ramesh") with
        {
            Fields = new Dictionary<Guid, string?> { [members.Id] = "many" },
        }, HttpStatusCode.BadRequest);
        await AssertBadAsync(client, contact.Id, Input(contact.PhoneNumber, "Ramesh") with
        {
            Fields = new Dictionary<Guid, string?> { [relation.Id] = "Cousin" },
        }, HttpStatusCode.BadRequest);

        // A blank value removes it; the others stay.
        var cleared = await SaveAsync(client, contact.Id, Input(contact.PhoneNumber, "Ramesh") with
        {
            Fields = new Dictionary<Guid, string?> { [city.Id] = "" },
        });
        Assert.False(cleared.Fields.ContainsKey(city.Id));
        Assert.Equal("Uncle", cleared.Fields[relation.Id]);

        // A choice still used cannot be taken away; renaming keeps the values.
        var shrink = await client.PutAsJsonAsync($"/api/v1/contact-fields/{relation.Id}", new UpdateContactFieldRequest("Relation", ["Aunt", "Friend"]), Ct);
        Assert.Equal(HttpStatusCode.Conflict, shrink.StatusCode);
        var renamed = await client.PutAsJsonAsync($"/api/v1/contact-fields/{relation.Id}", new UpdateContactFieldRequest("Relation to me", ["Uncle", "Aunt", "Friend", "Cousin"]), Ct);
        Assert.Equal("relation_to_me", (await renamed.Content.ReadFromJsonAsync<ContactFieldDto>(Ct))!.Key);

        // Fields are private; another user's field cannot be filled.
        var other = await ClientAsync();
        Assert.Empty((await other.GetFromJsonAsync<List<ContactFieldDto>>("/api/v1/contact-fields", Ct))!);
        var stranger = await other.PostAsJsonAsync("/api/v1/contacts", Input(ApiFactory.UniquePhone(), "X") with
        {
            Fields = new Dictionary<Guid, string?> { [city.Id] = "Surat" },
        }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);

        // Deleting a field removes its values.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/contact-fields/{members.Id}", Ct)).StatusCode);
        Assert.False((await client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}", Ct))!.Fields.ContainsKey(members.Id));
    }

    [Fact]
    public async Task Fields_fill_messages_and_people_without_a_value_are_left_out_and_contacts_are_found_by_value()
    {
        var client = await ClientAsync();
        var city = await FieldAsync(client, "City", "text");
        var joined = await FieldAsync(client, "Joined on", "date");
        var rajkot = await SaveAsync(client, null, Input(ApiFactory.UniquePhone(), "Ramesh") with
        {
            Fields = new Dictionary<Guid, string?> { [city.Id] = "Rajkot", [joined.Id] = "2019-03-05" },
        });
        var surat = await SaveAsync(client, null, Input(ApiFactory.UniquePhone(), "Sita") with
        {
            Fields = new Dictionary<Guid, string?> { [city.Id] = "Surat" },
        });
        var nobody = await SaveAsync(client, null, Input(ApiFactory.UniquePhone(), "Kiran"));

        var input = new BroadcastInput("Meet", null, null, null, null, null, [rajkot.Id, surat.Id, nobody.Id],
            "Dear {{name}}, see you in {{city}} (member since {{joined_on}}). {{unknown}}");
        var response = await client.PostAsJsonAsync($"/api/v1/broadcasts/preview?for={rajkot.Id}", input, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<BroadcastPreviewDto>(Ct))!;
        Assert.Equal(3, preview.Total);
        Assert.Equal(1, preview.WillSend);
        Assert.Equal("Dear Ramesh, see you in Rajkot (member since 5 Mar 2019). {{unknown}}", preview.Person!.Text);
        Assert.Contains(preview.Skipped, s => s.ContactId == surat.Id && s.Reason == "Needs details: no Joined on.");
        Assert.Contains(preview.Skipped, s => s.ContactId == nobody.Id && s.Reason == "Needs details: no City.");
        Assert.Equal(3, preview.People.Count);

        // The preview for someone who is left out says why.
        var forSurat = (await (await client.PostAsJsonAsync($"/api/v1/broadcasts/preview?for={surat.Id}", input, Ct))
            .Content.ReadFromJsonAsync<BroadcastPreviewDto>(Ct))!;
        Assert.Null(forSurat.Person!.Text);
        Assert.Equal("Needs details: no Joined on.", forSurat.Person.SkipReason);

        // Search by field value: text contains, other types exact, no value = everyone with a value.
        Assert.Equal(rajkot.Id, (await PageAsync(client, $"?fieldId={city.Id}&fieldValue=raj")).Items.Single().Id);
        Assert.Equal(2, (await PageAsync(client, $"?fieldId={city.Id}")).Total);
        Assert.Equal(rajkot.Id, (await PageAsync(client, $"?fieldId={joined.Id}&fieldValue=05-03-2019")).Items.Single().Id);
        Assert.Equal(0, (await PageAsync(client, $"?fieldId={joined.Id}&fieldValue=not-a-date")).Total);
    }

    [Fact]
    public async Task Excel_has_a_column_per_field_and_occasion_and_birthday_only_with_a_birthday_occasion()
    {
        var client = await ClientAsync();
        var city = await FieldAsync(client, "City", "text");
        var members = await FieldAsync(client, "Family members", "number");
        var asthi = await ModuleAsync(client, "Asthi", "yearly");

        // Without a Birthday occasion, an old file's Birthday is kept on the contact (and used once one is made).
        var phone = ApiFactory.UniquePhone();
        var csv = $"""
            Phone*,Name (English),Language,Birthday,Asthi,City,Family members
            {phone},Ramesh,en,15-08-1960,05-03,Rajkot,4
            {ApiFactory.UniquePhone()},Bad,en,,,Surat,four
            """;
        var result = await ImportAsync(client, csv);
        Assert.Equal((1, 1), (result.Created, result.Failed));
        Assert.Contains(result.Rows.Single(r => r.Action == "error").Errors, e => e.StartsWith("Family members", StringComparison.Ordinal));

        var ramesh = (await PageAsync(client, $"?search={phone[3..]}")).Items.Single();
        Assert.Equal("Rajkot", ramesh.Fields[city.Id]);
        Assert.Equal("4", ramesh.Fields[members.Id]);
        Assert.Equal(new ContactOccasionDto(asthi.Id, 5, 3, null, null), Assert.Single(ramesh.Occasions));
        var birthday = await ModuleAsync(client, "Birthday", "birthday");
        var withBirthday = (await client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{ramesh.Id}", Ct))!;
        Assert.Contains(new ContactOccasionDto(birthday.Id, 15, 8, 1960, null), withBirthday.Occasions);

        var template = await client.GetAsync("/api/v1/contacts/import/template", Ct);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
    }

    [Fact]
    public async Task The_excel_has_the_real_contacts_and_importing_it_back_updates_and_adds()
    {
        var client = await ClientAsync();
        var birthday = await ModuleAsync(client, "Birthday", "birthday");
        var asthi = await ModuleAsync(client, "Asthi", "yearly", "In memory of");
        var city = await FieldAsync(client, "City", "text");
        var joined = await FieldAsync(client, "Joined", "date");
        var phone = ApiFactory.UniquePhone();
        var saved = await SaveAsync(client, null, Input(phone, "Ramesh") with
        {
            Language = "gu",
            NameGu = "રમેશ",
            Occasions = [new(birthday.Id, 15, 8, 1960, null), new(asthi.Id, 5, 3, null, "Shantaben")],
            Fields = new Dictionary<Guid, string?> { [city.Id] = "Rajkot", [joined.Id] = "2019-03-05" },
        });

        // The download is the user's real data in the import's own columns, not sample rows.
        var bytes = await (await client.GetAsync("/api/v1/contacts/import/template", Ct)).Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Contacts");
            var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            string Cell(int row, string header) => sheet.Cell(row, headers.IndexOf(header) + 1).GetString();
            Assert.Equal(2, sheet.LastRowUsed()!.RowNumber());
            Assert.Equal(phone, Cell(2, "Phone*"));
            Assert.Equal("Gujarati", Cell(2, "Language"));
            Assert.Equal("15-08-1960", Cell(2, "Birthday"));
            Assert.Equal("05-03", Cell(2, "Asthi"));
            Assert.Equal("Shantaben", Cell(2, "Asthi - In memory of"));
            Assert.Equal("Rajkot", Cell(2, "City"));
            Assert.Equal("05-03-2019", Cell(2, "Joined"));

            // The user changes a cell, clears the language (kept as saved) and adds a new person.
            sheet.Cell(2, headers.IndexOf("City") + 1).SetValue("Surat");
            sheet.Cell(2, headers.IndexOf("Language") + 1).SetValue(string.Empty);
            sheet.Cell(3, headers.IndexOf("Phone*") + 1).SetValue(ApiFactory.UniquePhone());
            sheet.Cell(3, headers.IndexOf("Name (English)") + 1).SetValue("Suresh");
            sheet.Cell(3, headers.IndexOf("Asthi") + 1).SetValue("01-01");
            using var edited = new MemoryStream();
            workbook.SaveAs(edited);
            bytes = edited.ToArray();
        }

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", "my-contacts.xlsx");
        var response = await client.PostAsync("/api/v1/contacts/import?apply=true", content, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>(Ct))!;
        Assert.Equal((1, 1, 0), (result.Created, result.Updated, result.Failed));

        var ramesh = (await client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{saved.Id}", Ct))!;
        Assert.Equal("gu", ramesh.Language);
        Assert.Equal("Surat", ramesh.Fields[city.Id]);
        Assert.Equal("2019-03-05", ramesh.Fields[joined.Id]);
        Assert.Contains(new ContactOccasionDto(birthday.Id, 15, 8, 1960, null), ramesh.Occasions);
        Assert.Contains(new ContactOccasionDto(asthi.Id, 5, 3, null, "Shantaben"), ramesh.Occasions);
        var suresh = (await PageAsync(client, "?search=Suresh")).Items.Single();
        Assert.Equal(new ContactOccasionDto(asthi.Id, 1, 1, null, null), Assert.Single(suresh.Occasions));
    }

    [Fact]
    public async Task The_excel_offers_only_the_users_master_data_and_the_import_refuses_anything_else()
    {
        var client = await ClientAsync();
        (await client.PostAsJsonAsync("/api/v1/categories", new SaveCategoryRequest("Elders", "#10b981"), Ct)).EnsureSuccessStatusCode();
        var family = (await (await client.PostAsJsonAsync("/api/v1/groups", new SaveGroupRequest("Family"), Ct))
            .Content.ReadFromJsonAsync<GroupDto>(Ct))!;
        var shri = (await (await client.PostAsJsonAsync("/api/v1/salutations", new SaveSalutationRequest("Shri", "શ્રી"), Ct))
            .Content.ReadFromJsonAsync<SalutationDto>(Ct))!;
        Assert.Equal("Shri / શ્રી", shri.Label);
        var shop = await FieldAsync(client, "Shop", "choice", ["Retail", "Wholesale"]);
        var asthi = await ModuleAsync(client, "Asthi", "yearly", "In memory of");
        var phone = ApiFactory.UniquePhone();
        var saved = await SaveAsync(client, null, Input(phone, "Ramesh") with { SalutationId = shri.Id, GroupIds = [family.Id] });

        var bytes = await (await client.GetAsync("/api/v1/contacts/import/template", Ct)).Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Contacts");
            var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            int Col(string header) => headers.IndexOf(header) + 1;
            Assert.Contains("Group: Family", headers);
            Assert.DoesNotContain("Groups", headers);
            Assert.Equal("Shri / શ્રી", sheet.Cell(2, Col("Salutation")).GetString());
            Assert.Equal("Yes", sheet.Cell(2, Col("Group: Family")).GetString());

            // Pick-lists from the master data, on a hidden sheet; the header row is locked.
            Assert.Equal(ClosedXML.Excel.XLWorksheetVisibility.VeryHidden, workbook.Worksheet("Lists").Visibility);
            Assert.True(sheet.IsProtected);
            Assert.True(sheet.Cell(1, 1).Style.Protection.Locked);
            Assert.False(sheet.Cell(5, 1).Style.Protection.Locked);
            foreach (var header in new[] { "Category", "Language", "Salutation", "Group: Family", "Shop" })
            {
                var rule = sheet.Cell(3, Col(header)).GetDataValidation();
                Assert.Equal(ClosedXML.Excel.XLAllowedValues.List, rule.AllowedValues);
                Assert.Equal(ClosedXML.Excel.XLErrorStyle.Stop, rule.ErrorStyle);
            }
        }

        // A file with values pasted past the lists: every one is refused with its reason, nothing is made.
        var csv = $"""
            Phone*,Name (English),Category,Salutation,Group: Family,Groups,Shop,Asthi,Asthi - In memory of,Language
            {phone},Ramesh,Nobody,,,,,,,English
            {ApiFactory.UniquePhone()},A,,Shrimati,,,,,,English
            {ApiFactory.UniquePhone()},B,,,Maybe,,,,,English
            {ApiFactory.UniquePhone()},C,,,,Cousins,,,,English
            {ApiFactory.UniquePhone()},D,,,,,Online,,,English
            {ApiFactory.UniquePhone()},E,,,,,,,Shantaben,English
            {ApiFactory.UniquePhone()},F,Elders,shri,Yes,,wholesale,06-05-02,Shantaben,English
            """;
        var result = await ImportAsync(client, csv);
        Assert.Equal((1, 0, 6), (result.Created, result.Updated, result.Failed));
        Assert.Empty(result.NewCategories);
        Assert.Empty(result.NewGroups);
        string ErrorOf(int row) => Assert.Single(result.Rows.Single(r => r.Row == row).Errors);
        Assert.StartsWith("Category:", ErrorOf(2), StringComparison.Ordinal);
        Assert.StartsWith("Salutation:", ErrorOf(3), StringComparison.Ordinal);
        Assert.StartsWith("Group: Family:", ErrorOf(4), StringComparison.Ordinal);
        Assert.StartsWith("Groups:", ErrorOf(5), StringComparison.Ordinal);
        Assert.Contains("Shop", ErrorOf(6), StringComparison.Ordinal);
        Assert.StartsWith("Asthi:", ErrorOf(7), StringComparison.Ordinal);

        // The good row: every value from the lists, the 2-digit year read as 2002 (never in the future).
        var f = (await PageAsync(client, "?search=F")).Items.Single(c => c.NameEn == "F");
        Assert.Equal(shri.Id, f.SalutationId);
        Assert.Equal("Family", Assert.Single(f.Groups).Name);
        Assert.Equal("Wholesale", f.Fields[shop.Id]);
        Assert.Equal(new ContactOccasionDto(asthi.Id, 6, 5, 2002, "Shantaben"), Assert.Single(f.Occasions));
        var unchanged = (await client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{saved.Id}", Ct))!;
        Assert.Equal(shri.Id, unchanged.SalutationId);
    }

    private async Task<HttpClient> ClientAsync() => await factory.CreateClientForAsync(await factory.CreateUserAsync(UserKind.TenantAdmin));

    private static ContactInput Input(string phone, string name) => new(phone, null, name, null, null, "en", null, null, null, null, null);

    private static async Task<ContactDto> SaveAsync(HttpClient client, Guid? id, ContactInput input)
    {
        var response = id is { } existing
            ? await client.PutAsJsonAsync($"/api/v1/contacts/{existing}", input, Ct)
            : await client.PostAsJsonAsync("/api/v1/contacts", input, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ContactDto>(Ct))!;
    }

    private static async Task AssertBadAsync(HttpClient client, Guid id, ContactInput input, HttpStatusCode expected) =>
        Assert.Equal(expected, (await client.PutAsJsonAsync($"/api/v1/contacts/{id}", input, Ct)).StatusCode);

    private static async Task<ModuleDto> ModuleAsync(HttpClient client, string name, string kind, string? label = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/modules", new CreateModuleRequest(name, kind, label), Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ModuleDto>(Ct))!;
    }

    private static Task<HttpResponseMessage> PostFieldAsync(HttpClient client, string name, string type, string[]? options = null) =>
        client.PostAsJsonAsync("/api/v1/contact-fields", new CreateContactFieldRequest(name, type, options), Ct);

    private static async Task<ContactFieldDto> FieldAsync(HttpClient client, string name, string type, string[]? options = null)
    {
        var response = await PostFieldAsync(client, name, type, options);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ContactFieldDto>(Ct))!;
    }

    private static async Task<ContactPageDto> PageAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<ContactPageDto>("/api/v1/contacts" + query, Ct))!;

    private static async Task<ImportResultDto> ImportAsync(HttpClient client, string csv)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "contacts.csv");
        var response = await client.PostAsync("/api/v1/contacts/import?apply=true", content, Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ImportResultDto>(Ct))!;
    }
}
