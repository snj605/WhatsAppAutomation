using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Templates;

public sealed record TemplateDto(Guid Id, string Name, string Text, DateTimeOffset UpdatedAt)
{
    public static TemplateDto From(MessageTemplate t) => new(t.Id, t.Name, t.Text, t.UpdatedAt);
}

public static class TemplateErrors
{
    public static readonly Error NotSignedIn = new("templates.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);

    public static readonly Error NotFound = new("templates.not_found", "This template does not exist.", ErrorKind.NotFound);

    public static readonly Error Duplicate = new("templates.duplicate", "You already have a template with this name.", ErrorKind.Conflict);

    public static readonly Error TooMany = new("templates.too_many", "You can keep up to 200 templates. Delete some first.", ErrorKind.Conflict);
}

/// <summary>The user's own templates, by name.</summary>
public sealed record GetTemplatesQuery : IQuery<IReadOnlyList<TemplateDto>>;

internal sealed class GetTemplatesQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetTemplatesQuery, IReadOnlyList<TemplateDto>>
{
    public async Task<Result<IReadOnlyList<TemplateDto>>> Handle(GetTemplatesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TemplateErrors.NotSignedIn;
        }

        var list = await db.MessageTemplates.AsNoTracking().Where(t => t.UserId == userId).OrderBy(t => t.Name).ToListAsync(cancellationToken);
        return list.Select(TemplateDto.From).ToList();
    }
}

/// <summary>Creates a template (Id null) or changes one of the user's own.</summary>
public sealed record SaveTemplateCommand(Guid? Id, string Name, string Text) : ICommand<TemplateDto>;

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

internal sealed class SaveTemplateCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<SaveTemplateCommand, TemplateDto>
{
    private const int MaxPerUser = 200;

    public async Task<Result<TemplateDto>> Handle(SaveTemplateCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TemplateErrors.NotSignedIn;
        }

        var name = command.Name.Trim();
        if (await db.MessageTemplates.AnyAsync(
                t => t.UserId == userId && t.Id != command.Id && t.Name.ToLower() == name.ToLower(), cancellationToken))
        {
            return TemplateErrors.Duplicate;
        }

        var now = clock.GetUtcNow();
        MessageTemplate template;
        if (command.Id is { } id)
        {
            var existing = await db.MessageTemplates.SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);
            if (existing is null)
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

        await db.SaveChangesAsync(cancellationToken);
        return TemplateDto.From(template);
    }
}

/// <summary>Deletes one of the user's templates. Broadcasts already sent are not affected.</summary>
public sealed record DeleteTemplateCommand(Guid Id) : ICommand;

internal sealed class DeleteTemplateCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<DeleteTemplateCommand>
{
    public async Task<Result> Handle(DeleteTemplateCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TemplateErrors.NotSignedIn;
        }

        var deleted = await db.MessageTemplates.Where(t => t.Id == command.Id && t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? TemplateErrors.NotFound : Result.Success();
    }
}
