using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts.Queries;

public sealed record PreferencesDto(string DefaultLanguage);

/// <summary>The user's own settings. A user who never saved any gets English.</summary>
public sealed record GetPreferencesQuery : IQuery<PreferencesDto>;

internal sealed class GetPreferencesQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetPreferencesQuery, PreferencesDto>
{
    public async Task<Result<PreferencesDto>> Handle(GetPreferencesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        return new PreferencesDto(ContactLanguages.ToCode(await Preferences.DefaultLanguageAsync(db, userId, cancellationToken)));
    }
}

internal static class Preferences
{
    public const ContactLanguage Fallback = ContactLanguage.En;

    public static async Task<ContactLanguage> DefaultLanguageAsync(IAppDbContext db, Guid userId, CancellationToken cancellationToken) =>
        await db.UserPreferences.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => (ContactLanguage?)p.DefaultLanguage).SingleOrDefaultAsync(cancellationToken) ?? Fallback;
}
