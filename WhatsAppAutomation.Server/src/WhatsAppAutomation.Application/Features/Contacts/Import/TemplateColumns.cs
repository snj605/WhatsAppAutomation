using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Modules;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Contacts.Import;

/// <summary>A custom field as an import column: the header is the field's name.</summary>
internal sealed record FieldColumn(ContactField Field, string Key);

internal static class FieldColumns
{
    /// <summary>The user's fields whose header does not clash with a standard or occasion column.</summary>
    public static async Task<List<FieldColumn>> ForUserAsync(
        IAppDbContext db, Guid userId, IReadOnlyCollection<ModuleColumn> occasions, CancellationToken cancellationToken)
    {
        var fields = await db.ContactFields.AsNoTracking().Where(f => f.UserId == userId).OrderBy(f => f.SortOrder).ToListAsync(cancellationToken);
        var taken = ImportColumns.All.Select(c => c.Key)
            .Concat(occasions.Select(o => o.DateKey))
            .Concat(occasions.Where(o => o.ForNameKey is not null).Select(o => o.ForNameKey!))
            .ToHashSet();
        return [.. fields.Select(f => new FieldColumn(f, ImportColumns.KeyOf(f.Name))).Where(c => !taken.Contains(c.Key))];
    }

    public static string Help(ContactField f) => f.Type switch
    {
        ContactFieldType.Number => $"Your field \"{f.Name}\": a number. In messages: {{{{{f.Key}}}}}.",
        ContactFieldType.Date => $"Your field \"{f.Name}\": a date as DD-MM-YYYY. In messages: {{{{{f.Key}}}}}.",
        ContactFieldType.Choice => $"Your field \"{f.Name}\": one of {string.Join(", ", f.Options)}. In messages: {{{{{f.Key}}}}}.",
        _ => $"Your field \"{f.Name}\". In messages: {{{{{f.Key}}}}}.",
    };
}

/// <summary>The columns of the user's own import template.</summary>
/// <param name="WithBirthday">The Birthday column is there only for users with a Birthday occasion.</param>
/// <param name="Extra">One column per other occasion (and its second name) and per custom field.</param>
public sealed record ImportTemplateColumns(bool WithBirthday, IReadOnlyList<(string Header, string Help)> Extra);

public sealed record GetImportTemplateColumnsQuery : IQuery<ImportTemplateColumns>;

internal sealed class GetImportTemplateColumnsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetImportTemplateColumnsQuery, ImportTemplateColumns>
{
    public async Task<Result<ImportTemplateColumns>> Handle(GetImportTemplateColumnsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ContactErrors.NotSignedIn;
        }

        var withBirthday = await db.OccasionModules.AnyAsync(m => m.UserId == userId && m.Kind == ModuleKind.Birthday, cancellationToken);
        var occasions = await ModuleColumns.ForUserAsync(db, userId, cancellationToken);
        var fields = await FieldColumns.ForUserAsync(db, userId, occasions, cancellationToken);
        return new ImportTemplateColumns(
            withBirthday,
            [.. ModuleColumns.Headers(occasions), .. fields.Select(f => (f.Field.Name, FieldColumns.Help(f.Field)))]);
    }
}
