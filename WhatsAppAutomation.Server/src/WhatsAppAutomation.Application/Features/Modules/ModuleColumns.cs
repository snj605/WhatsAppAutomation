using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Contacts.Import;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Modules;

/// <summary>
/// The Excel columns of the user's own occasions: one date column per Yearly or Once module (named like the module),
/// plus one for the second name when the module has one ("Asthi visarjan - In memory of").
/// </summary>
internal sealed record ModuleColumn(Guid ModuleId, ModuleKind Kind, string Name, string DateKey, string? ForNameKey, string? ForNameHeader);

internal static class ModuleColumns
{
    public static async Task<List<ModuleColumn>> ForUserAsync(IAppDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var modules = await db.OccasionModules.AsNoTracking()
            .Where(m => m.UserId == userId && m.Kind != ModuleKind.Birthday)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);
        var reserved = ImportColumns.All.Select(c => c.Key).ToHashSet();
        return [.. modules
            .Where(m => !reserved.Contains(ImportColumns.KeyOf(m.Name)))
            .Select(m =>
            {
                var forHeader = m.ForNameLabel is null ? null : $"{m.Name} - {m.ForNameLabel}";
                return new ModuleColumn(m.Id, m.Kind, m.Name, ImportColumns.KeyOf(m.Name),
                    forHeader is null ? null : ImportColumns.KeyOf(forHeader), forHeader);
            })];
    }

    public static IReadOnlyList<(string Header, string Help)> Headers(IEnumerable<ModuleColumn> columns) =>
        [.. columns.SelectMany(c => c.ForNameHeader is null
            ? new[] { (c.Name, Help(c)) }
            : [(c.Name, Help(c)), (c.ForNameHeader, $"For \"{c.Name}\": the name used as {{{{for_name}}}} in its template.")])];

    private static string Help(ModuleColumn c) => c.Kind == ModuleKind.Once
        ? $"Your occasion \"{c.Name}\": the date as DD-MM-YYYY."
        : $"Your occasion \"{c.Name}\" (every year): DD-MM or DD-MM-YYYY.";
}

/// <summary>The occasion columns for the user's Excel template.</summary>
public sealed record GetModuleColumnsQuery : Common.Messaging.IQuery<IReadOnlyList<(string Header, string Help)>>;

internal sealed class GetModuleColumnsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : Common.Messaging.IQueryHandler<GetModuleColumnsQuery, IReadOnlyList<(string Header, string Help)>>
{
    public async Task<Common.Models.Result<IReadOnlyList<(string Header, string Help)>>> Handle(GetModuleColumnsQuery query, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? Common.Models.Result<IReadOnlyList<(string Header, string Help)>>.Success(
                ModuleColumns.Headers(await ModuleColumns.ForUserAsync(db, userId, cancellationToken)))
            : ModuleErrors.NotSignedIn;
}
