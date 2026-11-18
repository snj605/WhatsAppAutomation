using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Application.Features.Contacts.Commands;
using WhatsAppAutomation.Application.Features.Contacts.Import;
using WhatsAppAutomation.Application.Features.Contacts.Queries;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>The signed-in user's own contacts, categories and groups, and contact import.</summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class ContactsController(ISender mediator, IContactSpreadsheet spreadsheet) : ControllerBase
{
    /// <summary>Largest import file accepted.</summary>
    public const long MaxImportBytes = 5 * 1024 * 1024;

    // Categories

    [HttpGet("categories")]
    [HasPermission(Permissions.Contacts.View)]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetCategoriesQuery(), cancellationToken), Ok);

    [HttpPost("categories")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCategory(SaveCategoryRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SaveCategoryCommand(null, request.Name, request.Color), cancellationToken), Ok);

    [HttpPut("categories/{id:guid}")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCategory(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SaveCategoryCommand(id, request.Name, request.Color), cancellationToken), Ok);

    [HttpDelete("categories/{id:guid}")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new DeleteCategoryCommand(id), cancellationToken));

    /// <summary>Puts contacts in this category (add = true) or takes them out (they then have no category).</summary>
    [HttpPost("categories/{id:guid}/members")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeCategoryMembers(Guid id, GroupMembersRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(
            await mediator.Send(new ChangeCategoryMembersCommand(id, request.ContactIds ?? [], request.Add), cancellationToken), Ok);

    // Groups

    [HttpGet("groups")]
    [HasPermission(Permissions.Contacts.View)]
    [ProducesResponseType<IReadOnlyList<GroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGroups(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetGroupsQuery(), cancellationToken), Ok);

    [HttpPost("groups")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<GroupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateGroup(SaveGroupRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SaveGroupCommand(null, request.Name), cancellationToken), Ok);

    [HttpPut("groups/{id:guid}")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<GroupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateGroup(Guid id, SaveGroupRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SaveGroupCommand(id, request.Name), cancellationToken), Ok);

    [HttpDelete("groups/{id:guid}")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteGroup(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new DeleteGroupCommand(id), cancellationToken));

    [HttpPost("groups/{id:guid}/members")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<GroupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeGroupMembers(Guid id, GroupMembersRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new ChangeGroupMembersCommand(id, request.ContactIds ?? [], request.Add), cancellationToken), Ok);

    // Contacts

    [HttpGet("contacts")]
    [HasPermission(Permissions.Contacts.View)]
    [ProducesResponseType<ContactPageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContacts(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? groupId,
        [FromQuery] bool needsDetails = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? whatsApp = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(
            await mediator.Send(new GetContactsQuery(search, categoryId, groupId, needsDetails, page, pageSize, whatsApp), cancellationToken), Ok);

    /// <summary>How many contacts are on WhatsApp, not on WhatsApp, waiting for the check, or never checked.</summary>
    [HttpGet("contacts/whatsapp-check")]
    [HasPermission(Permissions.Contacts.View)]
    [ProducesResponseType<WhatsAppCheckSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWhatsAppCheck(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetWhatsAppCheckSummaryQuery(), cancellationToken), Ok);

    /// <summary>
    /// Queues numbers for the WhatsApp check (all never-checked ones, or the given contacts). The check runs in the
    /// background while WhatsApp is connected; the "contacts" live update says when results arrive.
    /// </summary>
    [HttpPost("contacts/whatsapp-check")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<WhatsAppCheckSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestWhatsAppCheck(WhatsAppCheckRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new RequestWhatsAppCheckCommand(request.ContactIds), cancellationToken), Ok);

    /// <summary>Contacts with a birthday today or in the next <paramref name="days"/> days (0–60), soonest first.</summary>
    [HttpGet("contacts/{id:guid}")]
    [HasPermission(Permissions.Contacts.View)]
    [ProducesResponseType<ContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContact(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetContactQuery(id), cancellationToken), Ok);

    [HttpPost("contacts")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<ContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateContact(ContactInput request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SaveContactCommand(null, request), cancellationToken), Ok);

    [HttpPut("contacts/{id:guid}")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<ContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateContact(Guid id, ContactInput request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SaveContactCommand(id, request), cancellationToken), Ok);

    [HttpPut("contacts/{id:guid}/opt-out")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<ContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetOptOut(Guid id, SetOptOutRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SetContactOptOutCommand(id, request.OptedOut), cancellationToken), Ok);

    [HttpPost("contacts/delete")]
    [HasPermission(Permissions.Contacts.Manage)]
    [ProducesResponseType<DeletedResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteContacts(DeleteContactsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(
            await mediator.Send(new DeleteContactsCommand(request.Ids ?? []), cancellationToken), n => Ok(new DeletedResponse(n)));

    // Import

    /// <summary>The .xlsx import template, with the user's own categories in the Category dropdown.</summary>
    [HttpGet("contacts/import/template")]
    [HasPermission(Permissions.Contacts.Import)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> GetImportTemplate(CancellationToken cancellationToken)
    {
        var categories = await mediator.Send(new GetCategoriesQuery(), cancellationToken);
        if (categories.Error is { } error)
        {
            return this.ToProblem(error);
        }

        var columns = await mediator.Send(new Application.Features.Modules.GetModuleColumnsQuery(), cancellationToken);
        var bytes = spreadsheet.CreateTemplate(categories.Value.Select(c => c.Name).ToList(), columns.IsSuccess ? columns.Value : []);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "contacts-template.xlsx");
    }

    /// <summary>
    /// Reads an .xlsx or .csv file. With apply=false (the default) nothing is saved and the result is a preview;
    /// send the same file again with apply=true to save the rows without errors.
    /// </summary>
    [HttpPost("contacts/import")]
    [HasPermission(Permissions.Contacts.Import)]
    [RequestSizeLimit(MaxImportBytes + (64 * 1024))]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportBytes + (64 * 1024))]
    [ProducesResponseType<ImportResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Import(IFormFile? file, [FromQuery] bool apply = false, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return this.ToProblem(Error.Validation("Choose a file to import."));
        }

        if (file.Length > MaxImportBytes)
        {
            return this.ToProblem(Error.Validation("The file is larger than 5 MB. Split it into smaller files."));
        }

        // ClosedXML needs a stream it can seek in.
        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        var result = await mediator.Send(new ImportContactsCommand(buffer, file.FileName, apply), cancellationToken);
        return this.ToActionResult(result, Ok);
    }

    // Preferences

    [HttpGet("preferences")]
    [Authorize]
    [ProducesResponseType<PreferencesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new GetPreferencesQuery(), cancellationToken), Ok);

    [HttpPut("preferences")]
    [Authorize]
    [ProducesResponseType<PreferencesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetPreferences(SetPreferencesRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new SetPreferencesCommand(request.DefaultLanguage), cancellationToken), Ok);
}
