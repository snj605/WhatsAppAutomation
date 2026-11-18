using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Files;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Templates;

/// <param name="Shared">In the tenant's library (everyone in the tenant may use it).</param>
/// <param name="CanEdit">The signed-in user may change or delete it.</param>
public sealed record TemplateDto(
    Guid Id, string Name, string Text, DateTimeOffset UpdatedAt, bool Shared = false, bool CanEdit = true, FileDto? Attachment = null);

public static class TemplateErrors
{
    public static readonly Error NotSignedIn = new("templates.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);

    public static readonly Error NotFound = new("templates.not_found", "This template does not exist.", ErrorKind.NotFound);

    public static readonly Error Duplicate = new("templates.duplicate", "You already have a template with this name.", ErrorKind.Conflict);

    public static readonly Error TooMany = new("templates.too_many", "You can keep up to 200 templates. Delete some first.", ErrorKind.Conflict);

    public static readonly Error OnlyAdminsShare =
        new("templates.only_admins_share", "Only tenant admins can put templates in the tenant's library.", ErrorKind.Forbidden);
}

/// <summary>Which templates a user may use and change.</summary>
internal static class TemplateAccess
{
    public sealed record Caller(Guid UserId, Guid? TenantId, bool IsTenantAdmin);

    public static async Task<Caller> CallerAsync(IAppDbContext db, IPermissionService permissions, Guid userId, CancellationToken cancellationToken) =>
        new(userId, await db.UserTenantAsync(userId, cancellationToken),
            (await permissions.GetPermissionsAsync(userId, cancellationToken)).Contains(Permissions.Members.Manage));

    /// <summary>The user's own templates and the tenant's library.</summary>
    public static IQueryable<MessageTemplate> Visible(IAppDbContext db, Caller c) =>
        db.MessageTemplates.Where(t => t.UserId == c.UserId || (t.IsShared && c.TenantId != null && t.TenantId == c.TenantId));

    /// <summary>Own templates, and (for tenant admins) the tenant's library.</summary>
    public static bool MayEdit(MessageTemplate t, Caller c) =>
        t.UserId == c.UserId || (t.IsShared && c.IsTenantAdmin && t.TenantId == c.TenantId);

    /// <summary>A template's text the user may send (own or from the library), for modules and the composer.</summary>
    public static async Task<MessageTemplate?> UsableAsync(IAppDbContext db, Guid userId, Guid templateId, CancellationToken cancellationToken)
    {
        var tenantId = await db.UserTenantAsync(userId, cancellationToken);
        return await db.MessageTemplates.AsNoTracking().SingleOrDefaultAsync(
            t => t.Id == templateId && (t.UserId == userId || (t.IsShared && tenantId != null && t.TenantId == tenantId)),
            cancellationToken);
    }

    public static async Task<List<TemplateDto>> ToDtosAsync(IAppDbContext db, List<MessageTemplate> templates, Caller c, CancellationToken cancellationToken)
    {
        var fileIds = templates.Where(t => t.AttachmentId != null).Select(t => t.AttachmentId!.Value).ToList();
        var files = await db.StoredFiles.AsNoTracking().Where(f => fileIds.Contains(f.Id) && (f.OnDisk || f.Data != null))
            .ToDictionaryAsync(f => f.Id, FileDto.From, cancellationToken);
        return [.. templates.Select(t => new TemplateDto(
            t.Id, t.Name, t.Text, t.UpdatedAt, t.IsShared, MayEdit(t, c), t.AttachmentId is { } a ? files.GetValueOrDefault(a) : null))];
    }
}

/// <summary>The user's own templates and the tenant's library, by name.</summary>
public sealed record GetTemplatesQuery : IQuery<IReadOnlyList<TemplateDto>>;

internal sealed class GetTemplatesQueryHandler(IAppDbContext db, ICurrentUser currentUser, IPermissionService permissions)
    : IQueryHandler<GetTemplatesQuery, IReadOnlyList<TemplateDto>>
{
    public async Task<Result<IReadOnlyList<TemplateDto>>> Handle(GetTemplatesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TemplateErrors.NotSignedIn;
        }

        var caller = await TemplateAccess.CallerAsync(db, permissions, userId, cancellationToken);
        var list = await TemplateAccess.Visible(db, caller).AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
        return await TemplateAccess.ToDtosAsync(db, list, caller, cancellationToken);
    }
}

/// <summary>
/// Creates a template (Id null) or changes one the user may change. Shared puts it in the tenant's library
/// (tenant admins only). AttachmentId: a file the user uploaded or took from a link; null removes the file.
/// </summary>
public sealed record SaveTemplateCommand(Guid? Id, string Name, string Text, bool Shared = false, Guid? AttachmentId = null) : ICommand<TemplateDto>;

internal sealed class SaveTemplateCommandValidator : AbstractValidator<SaveTemplateCommand>
{
    public SaveTemplateCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(MessageTemplate.NameMaxLength)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Give the template a name.");
        RuleFor(c => c.Text).NotEmpty().MaximumLength(MessageTemplate.TextMaxLength)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Write the message.");
    }
}

internal sealed class SaveTemplateCommandHandler(IAppDbContext db, ICurrentUser currentUser, IPermissionService permissions, TimeProvider clock)
    : ICommandHandler<SaveTemplateCommand, TemplateDto>
{
    private const int MaxPerUser = 200;

    public async Task<Result<TemplateDto>> Handle(SaveTemplateCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TemplateErrors.NotSignedIn;
        }

        var caller = await TemplateAccess.CallerAsync(db, permissions, userId, cancellationToken);
        if (command.Shared && (!caller.IsTenantAdmin || caller.TenantId is null))
        {
            return TemplateErrors.OnlyAdminsShare;
        }

        var name = command.Name.Trim();
        var duplicate = command.Shared
            ? await db.MessageTemplates.AnyAsync(
                t => t.IsShared && t.TenantId == caller.TenantId && t.Id != command.Id && t.Name.ToLower() == name.ToLower(), cancellationToken)
            : await db.MessageTemplates.AnyAsync(
                t => t.UserId == userId && !t.IsShared && t.Id != command.Id && t.Name.ToLower() == name.ToLower(), cancellationToken);
        if (duplicate)
        {
            return TemplateErrors.Duplicate;
        }

        if (command.AttachmentId is { } fileId &&
            await FileRules.UsableAsync(db, userId, caller.TenantId, fileId, cancellationToken) is null)
        {
            return FileErrors.NotFound;
        }

        var now = clock.GetUtcNow();
        MessageTemplate template;
        if (command.Id is { } id)
        {
            var existing = await TemplateAccess.Visible(db, caller).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (existing is null || !TemplateAccess.MayEdit(existing, caller))
            {
                return TemplateErrors.NotFound;
            }

            template = existing;
            template.Update(name, command.Text, now);
        }
        else
        {
            if (await db.MessageTemplates.CountAsync(t => t.UserId == userId, cancellationToken) >= MaxPerUser)
            {
                return TemplateErrors.TooMany;
            }

            template = new MessageTemplate(userId, name, command.Text, now);
            db.MessageTemplates.Add(template);
        }

        template.Share(caller.TenantId, command.Shared);
        template.SetAttachment(command.AttachmentId);
        await db.SaveChangesAsync(cancellationToken);
        return (await TemplateAccess.ToDtosAsync(db, [template], caller, cancellationToken)).Single();
    }
}

/// <summary>Deletes a template the user may change. Broadcasts already sent are not affected.</summary>
public sealed record DeleteTemplateCommand(Guid Id) : ICommand;

internal sealed class DeleteTemplateCommandHandler(IAppDbContext db, ICurrentUser currentUser, IPermissionService permissions)
    : ICommandHandler<DeleteTemplateCommand>
{
    public async Task<Result> Handle(DeleteTemplateCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TemplateErrors.NotSignedIn;
        }

        var caller = await TemplateAccess.CallerAsync(db, permissions, userId, cancellationToken);
        var template = await TemplateAccess.Visible(db, caller).SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
        if (template is null || !TemplateAccess.MayEdit(template, caller))
        {
            return TemplateErrors.NotFound;
        }

        db.MessageTemplates.Remove(template);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
