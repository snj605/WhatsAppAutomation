using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Contacts.Queries;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts.Commands;

/// <summary>Saves the user's default message language.</summary>
public sealed record SetPreferencesCommand(string DefaultLanguage) : ICommand<PreferencesDto>;

internal sealed class SetPreferencesCommandValidator : AbstractValidator<SetPreferencesCommand>
{
    public SetPreferencesCommandValidator() =>
        RuleFor(c => c.DefaultLanguage).Must(l => ContactLanguages.Parse(l) is not null).WithMessage("Language must be en, hi or gu.");
}

internal sealed class SetPreferencesCommandHandler(IAppDbContext db, ICurrentUser currentUser) : ICommandHandler<SetPreferencesCommand, PreferencesDto>
{
    public async Task<Result<PreferencesDto>> Handle(SetPreferencesCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var language = ContactLanguages.Parse(command.DefaultLanguage)!.Value;
        var preferences = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (preferences is null)
        {
            db.UserPreferences.Add(new UserPreferences(userId, language));
        }
        else
        {
            preferences.DefaultLanguage = language;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new PreferencesDto(ContactLanguages.ToCode(language));
    }
}
